// Drives one update and prints what it did, for the C# test to assert against.
//
// The updater under test is the shipped one - lib/swift/tabbit/Updater.swift - compiled
// beside this file exactly as a consumer's project would compile it, with the reader next
// to it because the updater reports its failures as the reader's error type.
//
// No package: the updater takes Foundation and nothing else. Whether that is still true is
// half of what compiling this file asks.

import Foundation

let arguments = CommandLine.arguments

guard arguments.count >= 3 else {
    FileHandle.standardError.write(Data("usage: main <base-url> <cache-directory>\n".utf8))
    exit(2)
}

var options = TabbitUpdater.Options()

// Short, because the retry test would otherwise spend its time asleep.
options.retryDelay = 0.05

// To standard error, so that standard output carries the result and nothing else.
options.log = { message in
    FileHandle.standardError.write(Data((message + "\n").utf8))
}

let result = TabbitUpdater.update(arguments[1], cacheDirectory: arguments[2], options: options)

/// A JSON string, or null. Enough escaping for a path and a message.
func quote(_ value: String?) -> String {
    guard let value = value else { return "null" }

    var text = "\""

    for scalar in value.unicodeScalars {
        switch scalar {
        case "\"": text += "\\\""
        case "\\": text += "\\\\"
        case "\n": text += "\\n"
        case "\r": text += "\\r"
        case "\t": text += "\\t"
        default:
            if scalar.value < 0x20 {
                text += String(format: "\\u%04x", scalar.value)
            } else {
                text.unicodeScalars.append(scalar)
            }
        }
    }

    return text + "\""
}

var json = "{"
json += "\"succeeded\":\(result.succeeded)"
json += ",\"error\":\(quote(result.error))"
json += ",\"upToDate\":\(result.upToDate)"
json += ",\"downloadedCount\":\(result.downloadedCount)"
json += ",\"downloadedBytes\":\(result.downloadedBytes)"
json += ",\"deletedCount\":\(result.deletedCount)"
json += ",\"localPath\":\(quote(result.localPath))"
json += "}"

FileHandle.standardOutput.write(Data((json + "\n").utf8))
