// Drives one update and prints what it did, for the C# test to assert against.
//
// The updater under test is the shipped one - lib/rust/tabbit/updater.rs - copied into a
// crate of its own and built exactly as a consumer's crate would build it, `ureq` and all.

use std::path::PathBuf;
use std::time::Duration;

use rust_updater_harness::updater;

fn main() {
    let arguments: Vec<String> = std::env::args().collect();

    if arguments.len() < 3 {
        eprintln!("usage: harness <base-url> <cache-directory>");
        std::process::exit(2);
    }

    let options = updater::UpdateOptions {
        // Short, because the retry test would otherwise spend its time asleep.
        retry_delay: Duration::from_millis(50),
        ..Default::default()
    };

    let mut log = |message: &str| eprintln!("{}", message);

    let result = updater::update(
        &arguments[1],
        &PathBuf::from(&arguments[2]),
        &options,
        &mut log,
    );

    println!(
        "{{\"succeeded\":{},\"error\":{},\"upToDate\":{},\"downloadedCount\":{},\
         \"downloadedBytes\":{},\"deletedCount\":{},\"localPath\":{}}}",
        result.succeeded,
        quote(result.error.as_deref()),
        result.up_to_date,
        result.downloaded_count,
        result.downloaded_bytes,
        result.deleted_count,
        quote(Some(&result.local_path.to_string_lossy())),
    );
}

/// A JSON string, or null. Enough escaping for a path and a message.
fn quote(value: Option<&str>) -> String {
    match value {
        None => "null".to_string(),
        Some(text) => {
            let mut quoted = String::from("\"");

            for c in text.chars() {
                match c {
                    '"' => quoted.push_str("\\\""),
                    '\\' => quoted.push_str("\\\\"),
                    '\n' => quoted.push_str("\\n"),
                    '\r' => quoted.push_str("\\r"),
                    '\t' => quoted.push_str("\\t"),
                    c if (c as u32) < 0x20 => quoted.push_str(&format!("\\u{:04x}", c as u32)),
                    c => quoted.push(c),
                }
            }

            quoted.push('"');
            quoted
        }
    }
}
