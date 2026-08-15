//! Tabbit's data updater.
//!
//! Brings a local copy of the exported data up to date with a copy served over HTTP - a
//! CDN, a bucket, a patch server - so a running program can take new data without being
//! redeployed. Emitted beside the reader and reads nothing but the manifest, so it knows
//! nothing about the schema and never has to change when one does.
//!
//! The manifest is what the exporter already writes next to the data: one entry per file
//! with its size and MD5. Comparing it with the local copy is the whole of the diff, so a
//! run downloads what changed and nothing else.
//!
//! Three properties, because a patcher that fails badly is worse than one that does not
//! exist:
//!
//!   Nothing is replaced until everything has arrived and been checked. Files land in a
//!   staging directory first and the local manifest is written last, so an update killed
//!   halfway leaves the previous data readable and the next run redoes the difference.
//!
//!   Every file is checked against the hash the manifest gives for it, so a truncated
//!   transfer that a proxy reported as success does not reach the cache.
//!
//!   A transient failure is retried with a doubling backoff, and a permanent one is not.
//!
//! Reading is somebody else's job. This produces a directory, and the generated tables
//! read it.
//!
//! # The one place this crate has a dependency
//!
//! Rust's standard library has no HTTP client, so this module uses `ureq`. That is the
//! whole of it: the manifest is parsed by the small JSON reader at the bottom and the
//! digest is the MD5 below it, both written out rather than pulled in, so turning the
//! updater on adds exactly one line to Cargo.toml. Turn `WriteUpdater` off and the
//! generated crate has no dependencies at all, which is what it had before.

use std::collections::HashMap;
use std::fs;
use std::io::Read;
use std::path::{Path, PathBuf};
use std::thread;
use std::time::Duration;

/// What an update is allowed to do. Every value has a working default.
#[derive(Clone, Debug)]
pub struct UpdateOptions {
    /// The binary exporter writes manifest-binary.json; the JSON exporter writes
    /// manifest-json.json.
    pub manifest_file_name: String,

    /// The first attempt is included, so three is two retries.
    pub max_attempts: u32,

    /// Waited before the second attempt. Doubled for each attempt after it.
    pub retry_delay: Duration,

    pub request_timeout: Duration,
    pub verify_hash: bool,
}

impl Default for UpdateOptions {
    fn default() -> Self {
        Self {
            manifest_file_name: "manifest-binary.json".to_string(),
            max_attempts: 3,
            retry_delay: Duration::from_millis(500),
            request_timeout: Duration::from_secs(30),
            verify_hash: true,
        }
    }
}

/// What an update did.
#[derive(Clone, Debug)]
pub struct UpdateResult {
    pub succeeded: bool,
    pub error: Option<String>,
    pub up_to_date: bool,
    pub downloaded_count: u32,
    pub downloaded_bytes: u64,
    pub deleted_count: u32,

    /// The directory holding the data. Hand it to the generated `Tables::read_all`. Set
    /// even on failure, because the previous data is still there and still readable -
    /// which is the point of failing the way this does.
    pub local_path: PathBuf,
}

/// One file of the manifest, and the hash to check it by.
#[derive(Clone, Debug)]
pub struct ManifestEntry {
    pub name: String,
    pub size: i64,
    pub hash: String,
}

/// What went wrong, and whether trying again could go differently.
#[derive(Debug)]
enum Failure {
    /// The same request might survive a moment later.
    Transient(String),
    /// It would not.
    Permanent(String),
}

impl Failure {
    fn message(&self) -> &str {
        match self {
            Failure::Transient(message) | Failure::Permanent(message) => message,
        }
    }
}

/// Brings `cache_directory` up to date with the data served under `base_url`.
///
/// Does not return `Result`. Everything that can go wrong here - the network, the disk, a
/// file that arrived corrupt - is a condition the caller has to handle rather than a
/// defect, and the answer a caller wants is "no, and here is why, and your old data is
/// still there" rather than an error that has to be matched against.
///
/// `log` is called with one line of progress at a time; pass `|_| {}` for silence.
pub fn update(
    base_url: &str,
    cache_directory: &Path,
    options: &UpdateOptions,
    log: &mut dyn FnMut(&str),
) -> UpdateResult {
    let mut result = UpdateResult {
        succeeded: false,
        error: None,
        up_to_date: false,
        downloaded_count: 0,
        downloaded_bytes: 0,
        deleted_count: 0,
        local_path: cache_directory.to_path_buf(),
    };

    match run(base_url, cache_directory, options, log, &mut result) {
        Ok(()) => {
            result.succeeded = true;
        }
        Err(failure) => {
            // The previous data is untouched, so the caller can carry on with it.
            result.error = Some(failure.message().to_string());

            log(&format!("tabbit: update failed: {}", failure.message()));
        }
    }

    result
}

fn run(
    base_url: &str,
    cache_directory: &Path,
    options: &UpdateOptions,
    log: &mut dyn FnMut(&str),
    result: &mut UpdateResult,
) -> Result<(), Failure> {
    let manifest_bytes = download(
        &join_url(base_url, &options.manifest_file_name),
        options,
        log,
    )?;

    let manifest_text = String::from_utf8(manifest_bytes)
        .map_err(|_| Failure::Permanent("the manifest is not valid UTF-8.".to_string()))?;

    let remote = parse_manifest(&manifest_text).map_err(Failure::Permanent)?;
    let local = read_local_manifest(&cache_directory.join(&options.manifest_file_name));

    let by_name: HashMap<&str, &ManifestEntry> =
        local.iter().map(|entry| (entry.name.as_str(), entry)).collect();

    let wanted: Vec<&ManifestEntry> = remote
        .iter()
        .filter(|entry| {
            // The file's presence is checked as well as the manifest's word for it: a
            // cache somebody cleaned out by hand would otherwise never be refilled.
            match by_name.get(entry.name.as_str()) {
                Some(previous) => {
                    previous.hash != entry.hash
                        || !local_path(cache_directory, &entry.name).exists()
                }
                None => true,
            }
        })
        .collect();

    let served: HashMap<&str, ()> = remote.iter().map(|entry| (entry.name.as_str(), ())).collect();

    let gone: Vec<&str> = local
        .iter()
        .map(|entry| entry.name.as_str())
        .filter(|name| !served.contains_key(name))
        .collect();

    if wanted.is_empty() && gone.is_empty() {
        log("tabbit: already up to date.");

        result.up_to_date = true;
        return Ok(());
    }

    log(&format!(
        "tabbit: {} file(s) to fetch, {} to remove.",
        wanted.len(),
        gone.len()
    ));

    // Everything lands here first. Nothing the caller can read is touched until the last
    // file has arrived and been checked.
    let staging = cache_directory.join(".staging");

    fs::create_dir_all(cache_directory).map_err(disk)?;
    let _ = fs::remove_dir_all(&staging);
    fs::create_dir_all(&staging).map_err(disk)?;

    for entry in &wanted {
        let data = download(&join_url(base_url, &entry.name), options, log)?;

        if options.verify_hash && !entry.hash.is_empty() {
            let actual = md5_hex(&data);

            if !actual.eq_ignore_ascii_case(&entry.hash) {
                return Err(Failure::Permanent(format!(
                    "'{}' arrived with hash {}, and the manifest says {}. \
                     Nothing was replaced.",
                    entry.name, actual, entry.hash
                )));
            }
        }

        let staged = local_path(&staging, &entry.name);

        if let Some(parent) = staged.parent() {
            fs::create_dir_all(parent).map_err(disk)?;
        }

        fs::write(&staged, &data).map_err(disk)?;

        result.downloaded_bytes += data.len() as u64;
    }

    // From here on the update is applied. Nothing below reaches the network.
    for name in &gone {
        let target = local_path(cache_directory, name);

        if target.exists() {
            fs::remove_file(&target).map_err(disk)?;
        }

        result.deleted_count += 1;
    }

    for entry in &wanted {
        let target = local_path(cache_directory, &entry.name);

        if let Some(parent) = target.parent() {
            fs::create_dir_all(parent).map_err(disk)?;
        }

        if target.exists() {
            fs::remove_file(&target).map_err(disk)?;
        }

        fs::rename(local_path(&staging, &entry.name), &target).map_err(disk)?;

        result.downloaded_count += 1;
    }

    // Last, and that ordering is the recovery story: a run killed before this point leaves
    // a manifest describing the data that is still on disk, so the next run fetches the
    // same files again rather than believing it has them.
    fs::write(cache_directory.join(&options.manifest_file_name), &manifest_text).map_err(disk)?;

    let _ = fs::remove_dir_all(&staging);

    log(&format!(
        "tabbit: updated. {} fetched, {} removed.",
        result.downloaded_count, result.deleted_count
    ));

    Ok(())
}

fn disk(error: std::io::Error) -> Failure {
    Failure::Permanent(error.to_string())
}

/// Reads the entries out of a manifest's JSON.
pub fn parse_manifest(text: &str) -> Result<Vec<ManifestEntry>, String> {
    let manifest = json::parse(text)?;

    let items = manifest
        .get("Items")
        .and_then(json::Value::as_array)
        .ok_or_else(|| "the manifest has no Items array".to_string())?;

    let mut entries = Vec::new();

    for item in items {
        let name = match item.get("Name").and_then(json::Value::as_str) {
            Some(name) if !name.is_empty() => name.to_string(),
            _ => continue,
        };

        entries.push(ManifestEntry {
            name,
            size: item.get("Size").and_then(json::Value::as_f64).unwrap_or(0.0) as i64,
            hash: item
                .get("Hash")
                .and_then(json::Value::as_str)
                .unwrap_or("")
                .to_string(),
        });
    }

    Ok(entries)
}

// ------------------------------------------------------------------------ transfer

/// Fetches one URL, retrying what is worth retrying.
fn download(
    url: &str,
    options: &UpdateOptions,
    log: &mut dyn FnMut(&str),
) -> Result<Vec<u8>, Failure> {
    let mut delay = options.retry_delay;
    let attempts = options.max_attempts.max(1);

    for attempt in 1..=attempts {
        match fetch(url, options) {
            Ok(data) => return Ok(data),
            Err(Failure::Transient(message)) if attempt < attempts => {
                log(&format!(
                    "tabbit: {} Retrying in {:.1}s ({} of {}).",
                    message,
                    delay.as_secs_f64(),
                    attempt,
                    attempts
                ));

                thread::sleep(delay);

                // Doubling rather than a fixed wait: a server refusing because it is
                // overloaded is not helped by every client coming back at the same
                // interval.
                delay *= 2;
            }
            Err(failure) => return Err(failure),
        }
    }

    unreachable!("the loop returns on the last attempt")
}

fn fetch(url: &str, options: &UpdateOptions) -> Result<Vec<u8>, Failure> {
    let agent = ureq::AgentBuilder::new()
        .timeout_connect(options.request_timeout)
        .timeout_read(options.request_timeout)
        .build();

    let response = match agent.get(url).call() {
        Ok(response) => response,
        Err(ureq::Error::Status(status, _)) => {
            let message = format!("'{}' answered {}.", url, status);

            // 408 and 429 are the server asking for another attempt, and 5xx is it failing
            // on its own account. A 404 is an answer: retrying it costs three round trips
            // to hear the same thing.
            return Err(if status == 408 || status == 429 || (500..=599).contains(&status) {
                Failure::Transient(message)
            } else {
                Failure::Permanent(message)
            });
        }
        Err(error) => {
            // The request never got an answer - DNS, a refused connection, a timeout.
            return Err(Failure::Transient(format!(
                "'{}' could not be reached: {}.",
                url, error
            )));
        }
    };

    let mut body = Vec::new();

    response
        .into_reader()
        .read_to_end(&mut body)
        .map_err(|error| Failure::Transient(format!("'{}' ended early: {}.", url, error)))?;

    Ok(body)
}

// ---------------------------------------------------------------------------- disk

/// Reads the cached manifest.
///
/// A missing or unreadable one is an empty manifest, which makes the next update fetch
/// everything - the safe direction to be wrong in.
fn read_local_manifest(file: &Path) -> Vec<ManifestEntry> {
    fs::read_to_string(file)
        .ok()
        .and_then(|text| parse_manifest(&text).ok())
        .unwrap_or_default()
}

/// A manifest name resolved under a directory, with its forward slashes honoured.
fn local_path(directory: &Path, name: &str) -> PathBuf {
    let mut resolved = directory.to_path_buf();

    for part in name.split('/').filter(|part| !part.is_empty()) {
        resolved.push(part);
    }

    resolved
}

/// Joins a base URL and a file name.
///
/// Not a path join, which on Windows produces a backslash and a URL no server will answer.
fn join_url(base_url: &str, name: &str) -> String {
    format!("{}/{}", base_url.trim_end_matches('/'), name.replace('\\', "/"))
}

// ----------------------------------------------------------------------------- MD5

/// The MD5 of some bytes, in the lower-case hex the manifest carries.
///
/// Written out rather than taken from a crate, so that turning the updater on costs the
/// consumer's Cargo.toml one dependency and not three. A wrong one could not hide either -
/// every download would fail its hash check on the first run.
pub fn md5_hex(input: &[u8]) -> String {
    const SHIFTS: [u32; 64] = [
        7, 12, 17, 22, 7, 12, 17, 22, 7, 12, 17, 22, 7, 12, 17, 22, //
        5, 9, 14, 20, 5, 9, 14, 20, 5, 9, 14, 20, 5, 9, 14, 20, //
        4, 11, 16, 23, 4, 11, 16, 23, 4, 11, 16, 23, 4, 11, 16, 23, //
        6, 10, 15, 21, 6, 10, 15, 21, 6, 10, 15, 21, 6, 10, 15, 21,
    ];

    // floor(abs(sin(i + 1)) * 2^32), computed rather than tabulated: the table is
    // sixty-four magic numbers and the one part of MD5 a transcription error hides in.
    let sines: Vec<u32> = (0..64)
        .map(|i| ((f64::from(i + 1)).sin().abs() * 4294967296.0) as u32)
        .collect();

    let bit_length = (input.len() as u64).wrapping_mul(8);

    // A 0x80 byte, zeroes, and the original bit length as a little-endian 64-bit number.
    let mut block = input.to_vec();
    block.push(0x80);

    while block.len() % 64 != 56 {
        block.push(0);
    }

    block.extend_from_slice(&bit_length.to_le_bytes());

    let (mut a0, mut b0, mut c0, mut d0) =
        (0x6745_2301u32, 0xefcd_ab89u32, 0x98ba_dcfeu32, 0x1032_5476u32);

    for chunk in block.chunks_exact(64) {
        let mut words = [0u32; 16];

        for (i, word) in words.iter_mut().enumerate() {
            *word = u32::from_le_bytes([
                chunk[i * 4],
                chunk[i * 4 + 1],
                chunk[i * 4 + 2],
                chunk[i * 4 + 3],
            ]);
        }

        let (mut a, mut b, mut c, mut d) = (a0, b0, c0, d0);

        for i in 0..64 {
            let (f, g) = match i {
                0..=15 => ((b & c) | (!b & d), i),
                16..=31 => ((d & b) | (!d & c), (5 * i + 1) % 16),
                32..=47 => (b ^ c ^ d, (3 * i + 5) % 16),
                _ => (c ^ (b | !d), (7 * i) % 16),
            };

            let f = f
                .wrapping_add(a)
                .wrapping_add(sines[i])
                .wrapping_add(words[g]);

            a = d;
            d = c;
            c = b;
            b = b.wrapping_add(f.rotate_left(SHIFTS[i]));
        }

        a0 = a0.wrapping_add(a);
        b0 = b0.wrapping_add(b);
        c0 = c0.wrapping_add(c);
        d0 = d0.wrapping_add(d);
    }

    let mut hex = String::with_capacity(32);

    for value in [a0, b0, c0, d0] {
        for byte in value.to_le_bytes() {
            hex.push_str(&format!("{:02x}", byte));
        }
    }

    hex
}

// ---------------------------------------------------------------------------- JSON

/// As much of JSON as reading a manifest needs, which turns out to be all of it.
///
/// Rust's standard library has no JSON reader, and pulling `serde` and `serde_json` into
/// a consumer's build for one small object is a heavier answer than the grammar is. Whole
/// rather than special-cased: a parser that understands JSON is shorter than one that
/// guesses at a shape and has to defend against every way the guess can be wrong.
mod json {
    use std::collections::HashMap;

    #[derive(Clone, Debug, PartialEq)]
    pub enum Value {
        Null,
        Bool(bool),
        Number(f64),
        String(String),
        Array(Vec<Value>),
        Object(HashMap<String, Value>),
    }

    impl Value {
        pub fn get(&self, key: &str) -> Option<&Value> {
            match self {
                Value::Object(fields) => fields.get(key),
                _ => None,
            }
        }

        pub fn as_str(&self) -> Option<&str> {
            match self {
                Value::String(text) => Some(text),
                _ => None,
            }
        }

        pub fn as_f64(&self) -> Option<f64> {
            match self {
                Value::Number(number) => Some(*number),
                _ => None,
            }
        }

        pub fn as_array(&self) -> Option<&Vec<Value>> {
            match self {
                Value::Array(values) => Some(values),
                _ => None,
            }
        }
    }

    pub fn parse(text: &str) -> Result<Value, String> {
        let bytes: Vec<char> = text.chars().collect();
        let mut reader = Reader { text: &bytes, at: 0 };

        reader.skip_whitespace();
        let value = reader.read_value()?;
        reader.skip_whitespace();

        if reader.at != bytes.len() {
            return Err(format!("trailing text at offset {} in the manifest.", reader.at));
        }

        Ok(value)
    }

    struct Reader<'a> {
        text: &'a [char],
        at: usize,
    }

    impl<'a> Reader<'a> {
        fn read_value(&mut self) -> Result<Value, String> {
            match self.peek() {
                Some('{') => self.read_object(),
                Some('[') => self.read_array(),
                Some('"') => Ok(Value::String(self.read_string()?)),
                Some('t') => self.read_literal("true", Value::Bool(true)),
                Some('f') => self.read_literal("false", Value::Bool(false)),
                Some('n') => self.read_literal("null", Value::Null),
                Some(_) => self.read_number(),
                None => Err("the manifest ended early.".to_string()),
            }
        }

        fn read_object(&mut self) -> Result<Value, String> {
            let mut fields = HashMap::new();

            self.at += 1;
            self.skip_whitespace();

            if self.peek() == Some('}') {
                self.at += 1;
                return Ok(Value::Object(fields));
            }

            loop {
                self.skip_whitespace();

                let key = self.read_string()?;

                self.skip_whitespace();
                self.expect(':')?;
                self.skip_whitespace();

                fields.insert(key, self.read_value()?);

                self.skip_whitespace();

                match self.next()? {
                    '}' => return Ok(Value::Object(fields)),
                    ',' => continue,
                    found => {
                        return Err(format!(
                            "expected ',' or '}}' at offset {} in the manifest, not '{}'.",
                            self.at - 1,
                            found
                        ))
                    }
                }
            }
        }

        fn read_array(&mut self) -> Result<Value, String> {
            let mut values = Vec::new();

            self.at += 1;
            self.skip_whitespace();

            if self.peek() == Some(']') {
                self.at += 1;
                return Ok(Value::Array(values));
            }

            loop {
                self.skip_whitespace();

                values.push(self.read_value()?);

                self.skip_whitespace();

                match self.next()? {
                    ']' => return Ok(Value::Array(values)),
                    ',' => continue,
                    found => {
                        return Err(format!(
                            "expected ',' or ']' at offset {} in the manifest, not '{}'.",
                            self.at - 1,
                            found
                        ))
                    }
                }
            }
        }

        fn read_string(&mut self) -> Result<String, String> {
            self.expect('"')?;

            let mut value = String::new();

            loop {
                let c = self.next()?;

                if c == '"' {
                    return Ok(value);
                }

                if c != '\\' {
                    value.push(c);
                    continue;
                }

                match self.next()? {
                    '"' => value.push('"'),
                    '\\' => value.push('\\'),
                    '/' => value.push('/'),
                    'b' => value.push('\u{0008}'),
                    'f' => value.push('\u{000C}'),
                    'n' => value.push('\n'),
                    'r' => value.push('\r'),
                    't' => value.push('\t'),
                    'u' => {
                        if self.at + 4 > self.text.len() {
                            return Err("a \\u escape ran off the end of the manifest.".to_string());
                        }

                        let digits: String = self.text[self.at..self.at + 4].iter().collect();

                        let code = u32::from_str_radix(&digits, 16)
                            .map_err(|_| format!("'{}' is not a \\u escape.", digits))?;

                        value.push(char::from_u32(code).unwrap_or('\u{FFFD}'));
                        self.at += 4;
                    }
                    escape => {
                        return Err(format!("unknown escape '\\{}' in the manifest.", escape))
                    }
                }
            }
        }

        fn read_number(&mut self) -> Result<Value, String> {
            let start = self.at;

            while let Some(c) = self.peek() {
                if "+-.eE0123456789".contains(c) {
                    self.at += 1;
                } else {
                    break;
                }
            }

            if start == self.at {
                return Err(format!("expected a value at offset {} in the manifest.", self.at));
            }

            let text: String = self.text[start..self.at].iter().collect();

            text.parse::<f64>()
                .map(Value::Number)
                .map_err(|_| format!("'{}' is not a number.", text))
        }

        fn read_literal(&mut self, literal: &str, value: Value) -> Result<Value, String> {
            let found: String = self
                .text
                .iter()
                .skip(self.at)
                .take(literal.chars().count())
                .collect();

            if found != literal {
                return Err(format!(
                    "expected {} at offset {} in the manifest.",
                    literal, self.at
                ));
            }

            self.at += literal.chars().count();
            Ok(value)
        }

        fn skip_whitespace(&mut self) {
            while let Some(c) = self.peek() {
                if c.is_whitespace() {
                    self.at += 1;
                } else {
                    break;
                }
            }
        }

        fn peek(&self) -> Option<char> {
            self.text.get(self.at).copied()
        }

        fn next(&mut self) -> Result<char, String> {
            let c = self
                .peek()
                .ok_or_else(|| "the manifest ended early.".to_string())?;

            self.at += 1;
            Ok(c)
        }

        fn expect(&mut self, c: char) -> Result<(), String> {
            let found = self.next()?;

            if found == c {
                Ok(())
            } else {
                Err(format!(
                    "expected '{}' at offset {} in the manifest, not '{}'.",
                    c,
                    self.at - 1,
                    found
                ))
            }
        }
    }
}
