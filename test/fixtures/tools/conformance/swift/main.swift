// Conformance harness for the generated Swift reader.
//
// Reads Vectors.tcb through the generated accessor and prints each row in the canonical
// form described in ../README.md. No parsing here: the generated reader does that.

import Foundation

let arguments = CommandLine.arguments

guard arguments.count > 1 else {
    FileHandle.standardError.write(Data("usage: harness <binary-directory>\n".utf8))
    exit(1)
}

let data = ConformanceData()

// The corpus is signed, so the key goes in before the first read - which is the whole of
// what a consuming project does about the MAC. Without it the files would still load, and
// nothing here would notice: the check is the reader's, and it needs the key to run.
if let hex = ProcessInfo.processInfo.environment["TABBIT_TEST_TCB_MAC_KEY"], !hex.isEmpty {
    var key = [UInt8]()
    var index = hex.startIndex

    while index < hex.endIndex {
        let next = hex.index(index, offsetBy: 2)
        guard let byte = UInt8(hex[index ..< next], radix: 16) else {
            FileHandle.standardError.write(Data("the MAC key is not hexadecimal\n".utf8))
            exit(1)
        }

        key.append(byte)
        index = next
    }

    data.macKey = key
}

func quote(_ value: String) -> String {
    var quoted = "\""

    for scalar in value.unicodeScalars {
        switch scalar {
        case "\"": quoted += "\\\""
        case "\\": quoted += "\\\\"
        case "\n": quoted += "\\n"
        case "\r": quoted += "\\r"
        case "\t": quoted += "\\t"
        default:
            if scalar.value < 0x20 {
                quoted += String(format: "\\u%04x", scalar.value)
            } else {
                quoted.unicodeScalars.append(scalar)
            }
        }
    }

    return quoted + "\""
}

do {
    try data.readAll(arguments[1])

    var json = "["

    for (position, r) in data.vectors.records.enumerated() {
        if position > 0 { json += "," }

        json += "{"
        json += "\"index\":\(r.index),"
        json += "\"intVal\":\(r.intVal),"

        // A string, because JSON's single numeric type would round anything past 2^53.
        json += "\"bigVal\":\"\(r.bigVal)\","

        json += "\"floatVal\":\(r.floatVal),"
        json += "\"doubleVal\":\(r.doubleVal),"
        json += "\"text\":\(quote(r.text)),"
        json += "\"flag\":\(r.flag),"

        // Ticks, which is what the generated fields hold.
        json += "\"when\":\"\(r.when)\","
        json += "\"span\":\"\(r.span)\","

        json += "\"uid\":\"\(r.uid)\","
        json += "\"label\":\(r.label.value),"

        json += "\"ints\":[" + r.ints.map { "\($0)" }.joined(separator: ",") + "],"
        json += "\"strs\":[" + r.strs.map { quote($0) }.joined(separator: ",") + "],"

        // The two array forms whose element read is not the scalar one in a loop.
        json += "\"labels\":[" + r.labels.map { "\($0.value)" }.joined(separator: ",") + "],"
        json += "\"uids\":[" + r.uids.map { "\"\($0)\"" }.joined(separator: ",") + "]"

        // The reference indices, which is what the exporter writes for a foreign field.
        json += ",\"owner\":\(r.ownerIndex)"
        json += ",\"tier\":\(r.tierIndex)"

        // The three the v104 encodings win on.
        json += ",\"count\":\(r.count)"
        json += ",\"route\":\(quote(r.route))"
        json += ",\"zone\":\(quote(r.zone))"

        json += "}"
    }

    json += "]"

    // Written as bytes rather than printed: `print` goes through the platform's text
    // encoding, and on Windows that is a legacy codepage which would mangle every
    // non-ASCII value in the corpus.
    FileHandle.standardOutput.write(Data(json.utf8))
} catch {
    FileHandle.standardError.write(Data("\(error)\n".utf8))
    exit(1)
}
