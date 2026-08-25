// Conformance harness for the generated C# reader.
//
// Reads Vectors.tcb through the generated accessor and prints each row in the canonical
// form described in ../README.md. No parsing here: the generated reader does that.

using System;
using System.Globalization;
using System.Text;
using System.Threading.Tasks;
using Tabbit.Conformance;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        if (args.Length < 1)
        {
            Console.Error.WriteLine("usage: conformance-csharp <binary-directory>");
            return 1;
        }

        // The corpus is signed, so the key goes in before the first read - which is the
        // whole of what a consuming project does about the MAC. Without it the files would
        // still load, and nothing here would notice: the check is the reader's, and it
        // needs the key to run.
        string macKey = Environment.GetEnvironmentVariable("TABBIT_TEST_TCB_MAC_KEY");

        if (!string.IsNullOrEmpty(macKey))
            ConformanceAccessor.MacKey = Convert.FromHexString(macKey);

        await ConformanceAccessor.ReadAllAsync(args[0]);

        var json = new StringBuilder("[");

        for (int i = 0; i < ConformanceAccessor.Vectors.Records.Count; i++)
        {
            var r = ConformanceAccessor.Vectors.Records[i];

            if (i > 0)
                json.Append(',');

            json.Append('{');
            json.Append("\"index\":").Append(Number(r.Index)).Append(',');
            json.Append("\"intVal\":").Append(Number(r.IntVal)).Append(',');
            json.Append("\"bigVal\":\"").Append(Number(r.BigVal)).Append("\",");
            json.Append("\"floatVal\":").Append(Number(r.FloatVal)).Append(',');
            json.Append("\"doubleVal\":").Append(Number(r.DoubleVal)).Append(',');
            json.Append("\"text\":").Append(Quote(r.Text)).Append(',');
            json.Append("\"flag\":").Append(r.Flag ? "true" : "false").Append(',');
            json.Append("\"when\":\"").Append(Number(r.When.Ticks)).Append("\",");
            json.Append("\"span\":\"").Append(Number(r.Span.Ticks)).Append("\",");
            json.Append("\"uid\":\"").Append(r.Uid.ToString("D").ToLowerInvariant()).Append("\",");
            json.Append("\"label\":").Append(Number((int)r.Label)).Append(',');

            json.Append("\"ints\":[");
            for (int k = 0; k < r.Ints.Length; k++)
                json.Append(k > 0 ? "," : "").Append(Number(r.Ints[k]));
            json.Append("],");

            json.Append("\"strs\":[");
            for (int k = 0; k < r.Strs.Length; k++)
                json.Append(k > 0 ? "," : "").Append(Quote(r.Strs[k]));
            json.Append("],");

            // The two array forms whose element read is not the scalar one in a loop.
            json.Append("\"labels\":[");
            for (int k = 0; k < r.Labels.Length; k++)
                json.Append(k > 0 ? "," : "").Append(Number((int)r.Labels[k]));
            json.Append("],");

            json.Append("\"uids\":[");
            for (int k = 0; k < r.Uids.Length; k++)
                json.Append(k > 0 ? "," : "")
                    .Append('"').Append(r.Uids[k].ToString("D").ToLowerInvariant()).Append('"');
            json.Append(']');

            // The reference indices, which is what the exporter writes for a foreign field.
            json.Append(",\"owner\":").Append(r._owner_Owners_index);
            json.Append(",\"tier\":").Append(r._tier_Owners_index);

            // And one reference per element, printed as the stored index each came in as.
            json.Append(",\"owners\":[");
            for (int k = 0; k < r._owners_Owners_index.Length; k++)
                json.Append(k > 0 ? "," : "").Append(r._owners_Owners_index[k]);
            json.Append(']');

            // The three the v104 encodings win on.
            json.Append(",\"count\":").Append(Number(r.Count));
            json.Append(",\"route\":").Append(Quote(r.Route));
            json.Append(",\"zone\":").Append(Quote(r.Zone));

            json.Append('}');
        }

        json.Append(']');

        // UTF-8 without a byte order mark: the comparison reads this back as text, and a
        // mark would land inside the first token.
        Console.OutputEncoding = new UTF8Encoding(false);
        Console.Out.Write(json.ToString());
        return 0;
    }

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Number(long value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>Round-trip format, so the printed value is the one that was read.</summary>
    private static string Number(float value) => value.ToString("R", CultureInfo.InvariantCulture);

    private static string Number(double value) => value.ToString("R", CultureInfo.InvariantCulture);

    private static string Quote(string value)
    {
        var quoted = new StringBuilder("\"");

        foreach (var c in value ?? "")
        {
            if (c == '"')
                quoted.Append("\\\"");
            else if (c == '\\')
                quoted.Append("\\\\");
            else if (c == '\n')
                quoted.Append("\\n");
            else if (c == '\r')
                quoted.Append("\\r");
            else if (c == '\t')
                quoted.Append("\\t");
            else if (c < 0x20)
                quoted.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
            else
                quoted.Append(c);
        }

        return quoted.Append('"').ToString();
    }
}
