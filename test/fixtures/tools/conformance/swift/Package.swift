// swift-tools-version:5.9
//
// The manifest the Swift conformance harness builds through. Copied into the generated
// output beside `main.swift`, so the sources it names are the ones Tabbit just wrote.
//
// A package rather than a bare `swiftc` invocation, because this is the build that has the
// dependency: verifying the corpus MAC needs HMAC-SHA-256, which comes from CryptoKit on
// Apple platforms and from swift-crypto everywhere else. The gate that compiles the same
// output with no package at all is `Swift_compiles_with_no_crypto_package`, and the two
// together are what keep the reader's three crypto states honest.
// spec/targets/swift-language-support.md.
//
// `path: "."` with an explicit source list, matching what the generator's own manifest does:
// the output layout is flat so that dropping it into an existing project stays the simple
// case.
import PackageDescription

let package = Package(
    name: "harness",
    // CryptoKit answers HMAC-SHA-256 on Apple platforms and starts at macOS 10.15.
    // Without a floor SwiftPM picks an older one and the build fails on the HMAC call
    // rather than on anything that names the cause.
    platforms: [.macOS(.v10_15), .iOS(.v13), .tvOS(.v13), .watchOS(.v6)],
    dependencies: [
        .package(url: "https://github.com/apple/swift-crypto.git", from: "3.0.0")
    ],
    targets: [
        .executableTarget(
            name: "harness",
            dependencies: [.product(name: "Crypto", package: "swift-crypto")],
            path: ".",
            sources: [
                "tabbit",
                "ConformanceData.swift",
                "tables",
                "enums",
                "constants",
                "main.swift",
            ])
    ]
)
