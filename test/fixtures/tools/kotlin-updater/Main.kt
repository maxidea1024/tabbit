import java.nio.file.Paths
import java.time.Duration

import tabbit.TabbitUpdater

/**
 * Drives one update and prints what it did, for the C# test to assert against.
 *
 * The updater under test is the shipped one - lib/kotlin/tabbit/TabbitUpdater.kt -
 * compiled beside this file exactly as a consumer's project would compile it.
 */
fun main(args: Array<String>) {
    if (args.size < 2) {
        System.err.println("usage: Main <base-url> <cache-directory>")
        kotlin.system.exitProcess(2)
    }

    val options = TabbitUpdater.Options()

    // Short, because the retry test would otherwise spend its time asleep.
    options.retryDelay = Duration.ofMillis(50)
    options.log = { message -> System.err.println(message) }

    val result = TabbitUpdater.update(args[0], Paths.get(args[1]), options)

    println(
        "{" +
            "\"succeeded\":${result.succeeded}" +
            ",\"error\":${quote(result.error)}" +
            ",\"upToDate\":${result.upToDate}" +
            ",\"downloadedCount\":${result.downloadedCount}" +
            ",\"downloadedBytes\":${result.downloadedBytes}" +
            ",\"deletedCount\":${result.deletedCount}" +
            ",\"localPath\":${quote(result.localPath.toString())}" +
            "}"
    )
}

/** A JSON string, or null. Enough escaping for a path and a message. */
private fun quote(value: String?): String {
    if (value == null) return "null"

    val text = StringBuilder("\"")

    for (c in value) {
        when {
            c == '"' -> text.append("\\\"")
            c == '\\' -> text.append("\\\\")
            c == '\n' -> text.append("\\n")
            c == '\r' -> text.append("\\r")
            c == '\t' -> text.append("\\t")
            c.code < 0x20 -> text.append(String.format("\\u%04x", c.code))
            else -> text.append(c)
        }
    }

    return text.append('"').toString()
}
