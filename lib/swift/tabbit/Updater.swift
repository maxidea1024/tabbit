// Tabbit's data updater.
//
// Brings a local copy of the exported data up to date with a copy served over HTTP - a
// CDN, a bucket, a patch server - so a running program can take new data without being
// redeployed. Emitted beside the reader and reads nothing but the manifest, so it knows
// nothing about the schema and never has to change when one does.
//
// The manifest is what the exporter already writes next to the data: one entry per file
// with its size and MD5. Comparing it with the local copy is the whole of the diff, so a
// run downloads what changed and nothing else.
//
// Three properties, because a patcher that fails badly is worse than one that does not
// exist:
//
//   Nothing is replaced until everything has arrived and been checked. Files land in a
//   staging directory first and the local manifest is written last, so an update killed
//   halfway leaves the previous data readable and the next run redoes the difference.
//
//   Every file is checked against the hash the manifest gives for it, so a truncated
//   transfer that a proxy reported as success does not reach the cache.
//
//   A transient failure is retried with a doubling backoff, and a permanent one is not.
//
// Reading is somebody else's job. This produces a directory, and the generated tables
// read it.
//
// Foundation and nothing else. URLSession does the transfer - and on Linux and Windows it
// lives in FoundationNetworking, which is the one import here that has to be conditional.
// JSONSerialization reads the manifest, so unlike some of the other runtimes this one needs
// no JSON parser of its own. MD5 is written out below rather than taken from a crypto
// package, the same judgement the Rust updater makes about its own: one hash is cheaper to
// carry than a dependency the reader does not otherwise need.

import Foundation

#if canImport(FoundationNetworking)
import FoundationNetworking
#endif

public enum TabbitUpdater {

    /// What an update is allowed to do. Every value has a working default.
    public struct Options {

        /// The binary exporter writes manifest-binary.json; the JSON exporter writes
        /// manifest-json.json.
        public var manifestFileName: String = "manifest-binary.json"

        /// The first attempt is included, so three is two retries.
        public var maxAttempts: Int = 3

        /// Waited before the second attempt, in seconds. Doubled for each attempt after it.
        public var retryDelay: TimeInterval = 0.5

        public var requestTimeout: TimeInterval = 30

        public var verifyHash: Bool = true

        /// Called with one line of progress, when set.
        public var log: ((String) -> Void)? = nil

        public init() {}
    }

    /// What an update did.
    public struct Result {

        /// The directory holding the data. Hand it to the generated accessor's readAll. Set
        /// even on failure, because the previous data is still there and still readable -
        /// which is the point of failing the way this does.
        public let localPath: String

        public var succeeded: Bool = false
        public var error: String? = nil
        public var upToDate: Bool = false
        public var downloadedCount: Int = 0
        public var downloadedBytes: Int = 0
        public var deletedCount: Int = 0

        init(localPath: String) { self.localPath = localPath }
    }

    /// One file of the manifest, and the hash to check it by.
    public struct ManifestEntry {
        public let name: String
        public let size: Int64
        public let hash: String
    }

    /// What one request came back with, filled in by the transfer's completion.
    ///
    /// `@unchecked Sendable` because the safety is the semaphore's rather than the type's:
    /// the completion writes these three and signals, and the caller reads them only after
    /// waiting. A struct would not do - the closure would be mutating captured `var`s, which
    /// Swift 6 refuses.
    private final class Answer: @unchecked Sendable {
        var payload: Data? = nil
        var response: URLResponse? = nil
        var failure: Error? = nil
    }

    /// A failure the same request might survive a moment later.
    private struct TransientError: Error { let message: String }

    /// A failure retrying cannot help.
    private struct PermanentError: Error { let message: String }

    private static func describe(_ error: Error) -> String {
        switch error {
        case let transient as TransientError: return transient.message
        case let permanent as PermanentError: return permanent.message
        case let tcb as TcbError: return tcb.message
        default: return "\(error)"
        }
    }

    /// Brings `cacheDirectory` up to date with the data served under `baseUrl`.
    ///
    /// Does not throw. Everything that can go wrong here - the network, the disk, a file
    /// that arrived corrupt - is a condition the caller has to handle rather than a defect,
    /// and a patcher that throws into a service's start-up is one that gets wrapped in a
    /// bare `try?` that swallows the reason.
    public static func update(
        _ baseUrl: String,
        cacheDirectory: String,
        options: Options = Options()
    ) -> Result {
        var result = Result(localPath: cacheDirectory)
        let files = FileManager.default

        do {
            let manifestData = try download(
                joinUrl(baseUrl, options.manifestFileName), options)

            guard let manifestText = String(data: manifestData, encoding: .utf8) else {
                throw PermanentError(message: "the manifest is not UTF-8 text.")
            }

            let remote = try parseManifest(manifestText)
            let local = readLocalManifest(
                localPath(cacheDirectory, options.manifestFileName))

            var byName = [String: ManifestEntry]()
            for entry in local { byName[entry.name] = entry }

            let wanted = remote.filter { entry in
                guard let previous = byName[entry.name] else { return true }

                // The file's presence is checked as well as the manifest's word for it: a
                // cache somebody cleaned out by hand would otherwise never be refilled.
                return previous.hash != entry.hash
                    || !files.fileExists(atPath: localPath(cacheDirectory, entry.name))
            }

            let served = Set(remote.map { $0.name })
            let gone = local.map { $0.name }.filter { !served.contains($0) }

            if wanted.isEmpty && gone.isEmpty {
                options.log?("tabbit: already up to date.")

                result.succeeded = true
                result.upToDate = true
                return result
            }

            options.log?("tabbit: \(wanted.count) file(s) to fetch, \(gone.count) to remove.")

            // Everything lands here first. Nothing the caller can read is touched until the
            // last file has arrived and been checked.
            let staging = localPath(cacheDirectory, ".staging")

            try files.createDirectory(
                atPath: cacheDirectory, withIntermediateDirectories: true)
            deleteRecursively(staging)
            try files.createDirectory(atPath: staging, withIntermediateDirectories: true)

            for entry in wanted {
                let payload = try download(joinUrl(baseUrl, entry.name), options)

                if options.verifyHash && !entry.hash.isEmpty {
                    let actual = md5Hex([UInt8](payload))

                    if actual.lowercased() != entry.hash.lowercased() {
                        throw PermanentError(message:
                            "'\(entry.name)' arrived with hash \(actual), and the manifest "
                            + "says \(entry.hash). Nothing was replaced.")
                    }
                }

                let staged = localPath(staging, entry.name)

                try files.createDirectory(
                    atPath: (staged as NSString).deletingLastPathComponent,
                    withIntermediateDirectories: true)

                try payload.write(to: URL(fileURLWithPath: staged))

                result.downloadedBytes += payload.count
            }

            // From here on the update is applied. Nothing below reaches the network.
            for name in gone {
                let target = localPath(cacheDirectory, name)

                if files.fileExists(atPath: target) {
                    try files.removeItem(atPath: target)
                }

                result.deletedCount += 1
            }

            for entry in wanted {
                let target = localPath(cacheDirectory, entry.name)

                try files.createDirectory(
                    atPath: (target as NSString).deletingLastPathComponent,
                    withIntermediateDirectories: true)

                // Replaced rather than moved onto: Foundation refuses a move whose
                // destination exists, and on the second update every destination does.
                if files.fileExists(atPath: target) {
                    try files.removeItem(atPath: target)
                }

                try files.moveItem(atPath: localPath(staging, entry.name), toPath: target)

                result.downloadedCount += 1
            }

            // Last, and that ordering is the recovery story: a run killed before this point
            // leaves a manifest describing the data that is still on disk, so the next run
            // fetches the same files again rather than believing it has them.
            try manifestData.write(
                to: URL(fileURLWithPath: localPath(cacheDirectory, options.manifestFileName)))

            deleteRecursively(staging)

            options.log?(
                "tabbit: updated. \(result.downloadedCount) fetched, "
                + "\(result.deletedCount) removed.")

            result.succeeded = true
            return result
        } catch {
            // The previous data is untouched, so the caller can carry on with it.
            result.error = describe(error)

            options.log?("tabbit: update failed: \(result.error ?? "")")
            return result
        }
    }

    /// Reads the entries out of a manifest's JSON.
    public static func parseManifest(_ text: String) throws -> [ManifestEntry] {
        guard let data = text.data(using: .utf8) else {
            throw PermanentError(message: "the manifest is not UTF-8 text.")
        }

        let parsed = try JSONSerialization.jsonObject(with: data)

        guard let manifest = parsed as? [String: Any] else {
            throw PermanentError(message: "the manifest is not an object.")
        }

        guard let items = manifest["Items"] as? [Any] else {
            throw PermanentError(message: "the manifest has no Items array.")
        }

        var entries = [ManifestEntry]()

        for item in items {
            guard let fields = item as? [String: Any] else { continue }
            guard let name = fields["Name"] as? String, !name.isEmpty else { continue }

            let size = (fields["Size"] as? NSNumber)?.int64Value ?? 0
            let hash = fields["Hash"] as? String ?? ""

            entries.append(ManifestEntry(name: name, size: size, hash: hash))
        }

        return entries
    }

    // ------------------------------------------------------------------ transfer

    /// Fetches one URL, retrying what is worth retrying.
    private static func download(_ url: String, _ options: Options) throws -> Data {
        var delay = max(0, options.retryDelay)
        let attempts = max(1, options.maxAttempts)
        var attempt = 1

        while true {
            do {
                return try fetch(url, options)
            } catch let error as TransientError {
                if attempt >= attempts { throw error }

                options.log?(String(
                    format: "tabbit: %@ Retrying in %.1fs (%d of %d).",
                    error.message, delay, attempt, attempts))

                Thread.sleep(forTimeInterval: delay)

                // Doubling rather than a fixed wait: a server refusing because it is
                // overloaded is not helped by every client coming back at the same interval.
                delay *= 2
                attempt += 1
            }
        }
    }

    /// One request, waited for.
    ///
    /// URLSession has no synchronous form, and this call is synchronous because everything
    /// around it is: a data update happens at start-up or at a loading screen, where the
    /// caller's next line depends on it. The completion runs on the session's own queue
    /// rather than the caller's, so waiting here does not deadlock the thread that signals.
    private static func fetch(_ url: String, _ options: Options) throws -> Data {
        guard let target = URL(string: url) else {
            throw PermanentError(message: "'\(url)' is not a URL.")
        }

        var request = URLRequest(url: target)
        request.timeoutInterval = options.requestTimeout
        request.httpMethod = "GET"

        // A box rather than three captured locals. Swift 6 makes mutating a captured `var`
        // from a concurrently-executing closure an error, and it is right to: what makes
        // this safe is that nothing reads the fields until the semaphore has been signalled,
        // which the compiler cannot see. Saying so with `@unchecked Sendable` puts the claim
        // where a reader will find it.
        let answer = Answer()
        let finished = DispatchSemaphore(value: 0)

        let task = URLSession.shared.dataTask(with: request) { data, response, error in
            answer.payload = data
            answer.response = response
            answer.failure = error
            finished.signal()
        }

        task.resume()
        finished.wait()

        let payload = answer.payload
        let response = answer.response

        if let error = answer.failure {
            // The request never got an answer - DNS, a refused connection, a timeout.
            throw TransientError(
                message: "'\(url)' could not be reached: \(error.localizedDescription).")
        }

        guard let status = (response as? HTTPURLResponse)?.statusCode else {
            throw TransientError(message: "'\(url)' gave no HTTP response.")
        }

        if status >= 200 && status <= 299 {
            return payload ?? Data()
        }

        let message = "'\(url)' answered \(status)."

        // 408 and 429 are the server asking for another attempt, and 5xx is it failing on
        // its own account. A 404 is an answer: retrying it costs three round trips to hear
        // the same thing.
        if status == 408 || status == 429 || (status >= 500 && status <= 599) {
            throw TransientError(message: message)
        }

        throw PermanentError(message: message)
    }

    // --------------------------------------------------------------------- disk

    /// Reads the cached manifest.
    ///
    /// A missing or unreadable one is an empty manifest, which makes the next update fetch
    /// everything - the safe direction to be wrong in.
    private static func readLocalManifest(_ file: String) -> [ManifestEntry] {
        guard let data = FileManager.default.contents(atPath: file),
              let text = String(data: data, encoding: .utf8) else { return [] }

        return (try? parseManifest(text)) ?? []
    }

    /// A manifest name resolved under a directory, with its forward slashes honoured.
    private static func localPath(_ directory: String, _ name: String) -> String {
        var resolved = URL(fileURLWithPath: directory)

        for part in name.split(separator: "/") where !part.isEmpty {
            resolved = resolved.appendingPathComponent(String(part))
        }

        return resolved.path
    }

    private static func deleteRecursively(_ directory: String) {
        // Failure is not reported: this runs to clear a staging directory that may not be
        // there, and a directory that cannot be removed shows up as the next step failing
        // with a reason of its own.
        try? FileManager.default.removeItem(atPath: directory)
    }

    /// Joins a base URL and a file name.
    ///
    /// Not a path join, which on Windows produces a backslash and a URL no server will
    /// answer.
    private static func joinUrl(_ baseUrl: String, _ name: String) -> String {
        var base = baseUrl

        while base.hasSuffix("/") { base.removeLast() }

        return base + "/" + name.replacingOccurrences(of: "\\", with: "/")
    }

    // ---------------------------------------------------------------------- MD5

    /// The MD5 of some bytes, in the lower-case hex the manifest carries.
    ///
    /// RFC 1321, written out rather than taken from CryptoKit or swift-crypto. What the
    /// hash is for is catching a transfer that arrived short, not resisting an adversary -
    /// so the reason to reach for a crypto package is not present, and the reason not to
    /// is: this file would then require one on every platform, for a hundred lines of
    /// arithmetic. The reader's own MAC is the other way round, and
    /// spec/tcb-mac-and-signature.md says why.
    ///
    /// Every operation wraps. Swift traps on overflow, and MD5 is nothing but wrapping
    /// addition and rotation.
    public static func md5Hex(_ data: [UInt8]) -> String {
        var a0: UInt32 = 0x6745_2301
        var b0: UInt32 = 0xefcd_ab89
        var c0: UInt32 = 0x98ba_dcfe
        var d0: UInt32 = 0x1032_5476

        // The message, padded: a 0x80 byte, zeros to eight short of a block, then the
        // length in bits as a little-endian 64.
        var message = data
        message.append(0x80)

        while message.count % 64 != 56 { message.append(0) }

        let bits = UInt64(data.count) &* 8
        for shift in stride(from: 0, to: 64, by: 8) {
            message.append(UInt8(truncatingIfNeeded: bits &>> UInt64(shift)))
        }

        var block = [UInt32](repeating: 0, count: 16)

        for start in stride(from: 0, to: message.count, by: 64) {
            for word in 0 ..< 16 {
                let at = start + word * 4

                block[word] = UInt32(message[at])
                    | (UInt32(message[at + 1]) &<< 8)
                    | (UInt32(message[at + 2]) &<< 16)
                    | (UInt32(message[at + 3]) &<< 24)
            }

            var a = a0
            var b = b0
            var c = c0
            var d = d0

            for round in 0 ..< 64 {
                var f: UInt32
                var g: Int

                switch round / 16 {
                case 0:
                    f = (b & c) | (~b & d)
                    g = round
                case 1:
                    f = (d & b) | (~d & c)
                    g = (5 * round + 1) % 16
                case 2:
                    f = b ^ c ^ d
                    g = (3 * round + 5) % 16
                default:
                    f = c ^ (b | ~d)
                    g = (7 * round) % 16
                }

                f = f &+ a &+ md5Constants[round] &+ block[g]

                a = d
                d = c
                c = b
                b = b &+ rotate(f, md5Shifts[round])
            }

            a0 = a0 &+ a
            b0 = b0 &+ b
            c0 = c0 &+ c
            d0 = d0 &+ d
        }

        var hex = ""
        hex.reserveCapacity(32)

        for word in [a0, b0, c0, d0] {
            for shift in stride(from: 0, to: 32, by: 8) {
                let byte = UInt8(truncatingIfNeeded: word &>> UInt32(shift))
                hex.append(md5Hexits[Int(byte >> 4)])
                hex.append(md5Hexits[Int(byte & 0x0F)])
            }
        }

        return hex
    }

    private static func rotate(_ value: UInt32, _ by: UInt32) -> UInt32 {
        (value &<< by) | (value &>> (32 &- by))
    }

    private static let md5Hexits = Array("0123456789abcdef")

    /// The per-round shift amounts of RFC 1321.
    private static let md5Shifts: [UInt32] = [
        7, 12, 17, 22, 7, 12, 17, 22, 7, 12, 17, 22, 7, 12, 17, 22,
        5, 9, 14, 20, 5, 9, 14, 20, 5, 9, 14, 20, 5, 9, 14, 20,
        4, 11, 16, 23, 4, 11, 16, 23, 4, 11, 16, 23, 4, 11, 16, 23,
        6, 10, 15, 21, 6, 10, 15, 21, 6, 10, 15, 21, 6, 10, 15, 21,
    ]

    /// The sine-derived constants of RFC 1321.
    private static let md5Constants: [UInt32] = [
        0xd76a_a478, 0xe8c7_b756, 0x2420_70db, 0xc1bd_ceee,
        0xf57c_0faf, 0x4787_c62a, 0xa830_4613, 0xfd46_9501,
        0x6980_98d8, 0x8b44_f7af, 0xffff_5bb1, 0x895c_d7be,
        0x6b90_1122, 0xfd98_7193, 0xa679_438e, 0x49b4_0821,
        0xf61e_2562, 0xc040_b340, 0x265e_5a51, 0xe9b6_c7aa,
        0xd62f_105d, 0x0244_1453, 0xd8a1_e681, 0xe7d3_fbc8,
        0x21e1_cde6, 0xc337_07d6, 0xf4d5_0d87, 0x455a_14ed,
        0xa9e3_e905, 0xfcef_a3f8, 0x676f_02d9, 0x8d2a_4c8a,
        0xfffa_3942, 0x8771_f681, 0x6d9d_6122, 0xfde5_380c,
        0xa4be_ea44, 0x4bde_cfa9, 0xf6bb_4b60, 0xbebf_bc70,
        0x289b_7ec6, 0xeaa1_27fa, 0xd4ef_3085, 0x0488_1d05,
        0xd9d4_d039, 0xe6db_99e5, 0x1fa2_7cf8, 0xc4ac_5665,
        0xf429_2244, 0x432a_ff97, 0xab94_23a7, 0xfc93_a039,
        0x655b_59c3, 0x8f0c_cc92, 0xffef_f47d, 0x8584_5dd1,
        0x6fa8_7e4f, 0xfe2c_e6e0, 0xa301_4314, 0x4e08_11a1,
        0xf753_7e82, 0xbd3a_f235, 0x2ad7_d2bb, 0xeb86_d391,
    ]
}
