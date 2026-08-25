// Conformance harness for the generated Java reader.
//
// Reads Vectors.tcb through the generated accessor and prints each row in the canonical
// form described in ../README.md. No parsing here: the generated reader does that.

import java.io.PrintStream;
import java.nio.charset.StandardCharsets;

// The record is its own top level type now that the target writes a file per type, so it is
// imported rather than reached through the accessor as `ConformanceData.VectorsRecord`.
import conformance.ConformanceData;
import conformance.VectorsRecord;

public final class Harness {

    public static void main(String[] args) {
        if (args.length < 1) {
            System.err.println("usage: Harness <binary-directory>");
            System.exit(1);
        }

        // The corpus is signed, so the key goes in before the first read - which is the
        // whole of what a consuming project does about the MAC. Without it the files would
        // still load, and nothing here would notice: the check is the reader's, and it
        // needs the key to run.
        String macKey = System.getenv("TABBIT_TEST_TCB_MAC_KEY");

        if (macKey != null && !macKey.isEmpty()) {
            byte[] key = new byte[macKey.length() / 2];

            for (int at = 0; at < key.length; at++) {
                key[at] = (byte) Integer.parseInt(macKey.substring(at * 2, at * 2 + 2), 16);
            }

            ConformanceData.macKey = key;
        }

        ConformanceData data = new ConformanceData();
        data.readAll(args[0]);

        StringBuilder json = new StringBuilder("[");

        boolean first = true;
        for (VectorsRecord r : data.vectors.records()) {
            if (!first) {
                json.append(',');
            }
            first = false;

            json.append('{');
            json.append("\"index\":").append(r.index).append(',');
            json.append("\"intVal\":").append(r.intVal).append(',');

            // A string, because JSON's single numeric type would round anything past 2^53.
            json.append("\"bigVal\":\"").append(r.bigVal).append("\",");

            json.append("\"floatVal\":").append(r.floatVal).append(',');
            json.append("\"doubleVal\":").append(r.doubleVal).append(',');
            json.append("\"text\":").append(quote(r.text)).append(',');
            json.append("\"flag\":").append(r.flag).append(',');

            // Ticks, which is what the generated fields hold.
            json.append("\"when\":\"").append(r.when).append("\",");
            json.append("\"span\":\"").append(r.span).append("\",");

            json.append("\"uid\":\"").append(r.uid).append("\",");
            json.append("\"label\":").append(r.label.value()).append(',');

            json.append("\"ints\":[");
            for (int k = 0; k < r.ints.length; k++) {
                json.append(k > 0 ? "," : "").append(r.ints[k]);
            }
            json.append("],");

            json.append("\"strs\":[");
            for (int k = 0; k < r.strs.length; k++) {
                json.append(k > 0 ? "," : "").append(quote(r.strs[k]));
            }
            json.append("],");

            // The two array forms whose element read is not the scalar one in a loop.
            json.append("\"labels\":[");
            for (int k = 0; k < r.labels.length; k++) {
                json.append(k > 0 ? "," : "").append(r.labels[k].value());
            }
            json.append("],");

            json.append("\"uids\":[");
            for (int k = 0; k < r.uids.length; k++) {
                json.append(k > 0 ? "," : "").append('"').append(r.uids[k]).append('"');
            }
            json.append(']');

            // The reference indices, which is what the exporter writes for a foreign field.
            json.append(",\"owner\":").append(r.ownerIndex);
            json.append(",\"tier\":").append(r.tierIndex);

            // And one reference per element, printed as the stored index each came in as.
            json.append(",\"owners\":[");
            for (int k = 0; k < r.ownersIndex.length; k++)
                json.append(k > 0 ? "," : "").append(r.ownersIndex[k]);
            json.append(']');

            // The three the v104 encodings win on.
            json.append(",\"count\":").append(r.count);
            json.append(",\"route\":").append(quote(r.route));
            json.append(",\"zone\":").append(quote(r.zone));

            json.append('}');
        }

        json.append(']');

        // UTF-8 explicitly: the platform default on Windows is a legacy codepage and would
        // mangle every non-ASCII value in the corpus.
        PrintStream out = new PrintStream(System.out, true, StandardCharsets.UTF_8);
        out.print(json);
        out.flush();
    }

    private static String quote(String value) {
        StringBuilder quoted = new StringBuilder("\"");

        for (int i = 0; i < value.length(); i++) {
            char c = value.charAt(i);

            if (c == '"') {
                quoted.append("\\\"");
            } else if (c == '\\') {
                quoted.append("\\\\");
            } else if (c == '\n') {
                quoted.append("\\n");
            } else if (c == '\r') {
                quoted.append("\\r");
            } else if (c == '\t') {
                quoted.append("\\t");
            } else if (c < 0x20) {
                quoted.append(String.format("\\u%04x", (int) c));
            } else {
                quoted.append(c);
            }
        }

        return quoted.append('"').toString();
    }
}
