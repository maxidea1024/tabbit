import java.nio.file.Path;
import java.nio.file.Paths;
import java.time.Duration;

import tabbit.TabbitUpdater;

/**
 * Drives one update and prints what it did, for the C# test to assert against.
 *
 * The updater under test is the shipped one - lib/java/tabbit/TabbitUpdater.java -
 * compiled beside this file exactly as a consumer's project would compile it.
 */
public final class Main {
    public static void main(String[] args) {
        if (args.length < 2) {
            System.err.println("usage: Main <base-url> <cache-directory>");
            System.exit(2);
        }

        var options = new TabbitUpdater.Options();

        // Short, because the retry test would otherwise spend its time asleep.
        options.retryDelay = Duration.ofMillis(50);
        options.log = System.err::println;

        Path cache = Paths.get(args[1]);

        TabbitUpdater.Result result = TabbitUpdater.update(args[0], cache, options);

        System.out.println("{"
                + "\"succeeded\":" + result.succeeded
                + ",\"error\":" + quote(result.error)
                + ",\"upToDate\":" + result.upToDate
                + ",\"downloadedCount\":" + result.downloadedCount
                + ",\"downloadedBytes\":" + result.downloadedBytes
                + ",\"deletedCount\":" + result.deletedCount
                + ",\"localPath\":" + quote(result.localPath.toString())
                + "}");
    }

    /** A JSON string, or null. Enough escaping for a path and a message. */
    private static String quote(String value) {
        if (value == null) {
            return "null";
        }

        var text = new StringBuilder("\"");

        for (char c : value.toCharArray()) {
            switch (c) {
                case '"': text.append("\\\""); break;
                case '\\': text.append("\\\\"); break;
                case '\n': text.append("\\n"); break;
                case '\r': text.append("\\r"); break;
                case '\t': text.append("\\t"); break;
                default:
                    if (c < 0x20) {
                        text.append(String.format("\\u%04x", (int) c));
                    } else {
                        text.append(c);
                    }
            }
        }

        return text.append('"').toString();
    }
}
