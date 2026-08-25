// Conformance harness for the generated Kotlin reader.
//
// Reads Vectors.tcb through the generated accessor and prints each row in the canonical
// form described in ../README.md. No parsing here: the generated reader does that.

import java.io.PrintStream
import java.nio.charset.StandardCharsets

import conformance.ConformanceData

fun main(args: Array<String>) {
    if (args.isEmpty()) {
        System.err.println("usage: Harness <binary-directory>")
        kotlin.system.exitProcess(1)
    }

    // The corpus is signed, so the key goes in before the first read - which is the whole of
    // what a consuming project does about the MAC. Without it the files would still load,
    // and nothing here would notice: the check is the reader's, and it needs the key to run.
    val macKey = System.getenv("TABBIT_TEST_TCB_MAC_KEY")

    if (!macKey.isNullOrEmpty()) {
        ConformanceData.macKey = ByteArray(macKey.length / 2) {
            macKey.substring(it * 2, it * 2 + 2).toInt(16).toByte()
        }
    }

    ConformanceData.readAll(args[0])

    val json = StringBuilder("[")

    for ((position, r) in ConformanceData.vectors.records.withIndex()) {
        if (position > 0) json.append(',')

        json.append('{')
        json.append("\"index\":").append(r.index).append(',')
        json.append("\"intVal\":").append(r.intVal).append(',')

        // A string, because JSON's single numeric type would round anything past 2^53.
        json.append("\"bigVal\":\"").append(r.bigVal).append("\",")

        json.append("\"floatVal\":").append(r.floatVal).append(',')
        json.append("\"doubleVal\":").append(r.doubleVal).append(',')
        json.append("\"text\":").append(quote(r.text)).append(',')
        json.append("\"flag\":").append(r.flag).append(',')

        // Ticks, which is what the generated fields hold.
        json.append("\"when\":\"").append(r.`when`).append("\",")
        json.append("\"span\":\"").append(r.span).append("\",")

        json.append("\"uid\":\"").append(r.uid).append("\",")
        json.append("\"label\":").append(r.label.value).append(',')

        json.append("\"ints\":[")
        for ((i, value) in r.ints.withIndex()) {
            if (i > 0) json.append(',')
            json.append(value)
        }
        json.append("],")

        json.append("\"strs\":[")
        for ((i, value) in r.strs.withIndex()) {
            if (i > 0) json.append(',')
            json.append(quote(value))
        }
        json.append("],")

        // The two array forms whose element read is not the scalar one in a loop.
        json.append("\"labels\":[")
        for ((i, value) in r.labels.withIndex()) {
            if (i > 0) json.append(',')
            json.append(value.value)
        }
        json.append("],")

        json.append("\"uids\":[")
        for ((i, value) in r.uids.withIndex()) {
            if (i > 0) json.append(',')
            json.append('"').append(value).append('"')
        }
        json.append(']')

        // The reference indices, which is what the exporter writes for a foreign field.
        json.append(",\"owner\":").append(r.owner)
        json.append(",\"tier\":").append(r.tierIndex)

        // And one reference per element, printed as the stored index each came in as.
        json.append(",\"owners\":[")
        for (k in r.owners.indices)
            json.append(if (k > 0) "," else "").append(r.owners[k])
        json.append(']')

        // The three the v104 encodings win on.
        json.append(",\"count\":").append(r.count)
        json.append(",\"route\":").append(quote(r.route))
        json.append(",\"zone\":").append(quote(r.zone))

        json.append('}')
    }

    json.append(']')

    // UTF-8 explicitly: the platform default on Windows is a legacy codepage and would
    // mangle every non-ASCII value in the corpus.
    val out = PrintStream(System.out, true, StandardCharsets.UTF_8)
    out.print(json)
    out.flush()
}

private fun quote(value: String): String {
    val quoted = StringBuilder("\"")

    for (c in value) {
        when {
            c == '"' -> quoted.append("\\\"")
            c == '\\' -> quoted.append("\\\\")
            c == '\n' -> quoted.append("\\n")
            c == '\r' -> quoted.append("\\r")
            c == '\t' -> quoted.append("\\t")
            c.code < 0x20 -> quoted.append("\\u%04x".format(c.code))
            else -> quoted.append(c)
        }
    }

    return quoted.append('"').toString()
}
