// Skew harness for the generated C# reader.
//
// Reads one table out of a directory of .tcb files and prints what came back. The point
// is that the directory need not have been written by the schema this was generated from:
// a column added since is skipped, a column removed since keeps its default, a widened
// type is promoted, and an incompatible one is refused by name.
//
//     evolution-csharp <binary-directory> <table-name>
//
// Prints {"rows":[...]} on a successful read and {"error":"..."} on a refused one, and
// exits 0 either way - a refusal is an outcome to assert, not a harness failure.
//
// And the refresh form, which reads a loaded table a second time:
//
//     evolution-csharp --refresh <first-directory> <second-directory> <table-name>
//
// Prints {"first":[...],"second":[...]} when the second read went through, or
// {"first":[...],"error":"...","after":[...]} when it was refused - where `after` is what
// the table still holds. A refused refresh has to leave the previous rows in place; that
// is the whole assertion.

using System;
using System.Collections;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Tabbit.Evolution;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("usage: evolution-csharp <binary-directory> <table-name>");
            Console.Error.WriteLine(
                "       evolution-csharp --refresh <first-directory> <second-directory> <table-name>");
            return 1;
        }

        if (args[0] == "--refresh")
        {
            if (args.Length < 4)
            {
                Console.Error.WriteLine(
                    "usage: evolution-csharp --refresh <first-directory> <second-directory> <table-name>");
                return 1;
            }

            return await Refresh(args[1], args[2], args[3]);
        }

        string filename = Path.Combine(args[0], args[1] + ".tcb");

        try
        {
            IEnumerable rows = await Read(args[1], filename);

            var json = new StringBuilder("{\"rows\":[");
            bool first = true;

            foreach (object row in rows)
            {
                if (!first)
                    json.Append(',');

                // The generated record's own ToString is JSON of every field it has, which
                // is what lets one harness print two generations of the same table without
                // knowing what either of them looks like.
                json.Append(row.ToString());
                first = false;
            }

            json.Append("]}");

            Console.WriteLine(json.ToString());
            return 0;
        }
        catch (Exception e)
        {
            Console.WriteLine("{\"error\":" + Quote(e.Message) + "}");
            return 0;
        }
    }

    /// <summary>
    /// Reads one table, then reads it again over the top - a refresh.
    /// </summary>
    /// <remarks>
    /// The second read is the one being asked about. Whether it succeeds or is refused, the
    /// table has to hold one whole load afterwards: the new rows if it worked, the old ones if
    /// it did not. Never a mixture, and never nothing.
    /// </remarks>
    private static async Task<int> Refresh(string firstDir, string secondDir, string table)
    {
        object loaded = Table(table);

        await Read(loaded, Path.Combine(firstDir, table + ".tcb"));

        var json = new StringBuilder("{\"first\":");
        Rows(loaded, json);

        try
        {
            await Read(loaded, Path.Combine(secondDir, table + ".tcb"));

            json.Append(",\"second\":");
            Rows(loaded, json);
        }
        catch (Exception e)
        {
            json.Append(",\"error\":").Append(Quote(e.Message));

            // What the table holds now that the refresh has failed.
            json.Append(",\"after\":");
            Rows(loaded, json);
        }

        Console.WriteLine(json.Append('}').ToString());
        return 0;
    }

    /// <summary>An instance of one of the three tables both generations have.</summary>
    private static object Table(string table)
    {
        switch (table)
        {
            case "Evolution": return new EvolutionTable();
            case "Promoted": return new PromotedTable();
            case "Refused": return new RefusedTable();
            default: throw new ArgumentException($"No table called `{table}` in this generation.");
        }
    }

    private static Task Read(object table, string filename)
    {
        switch (table)
        {
            case EvolutionTable t: return t.ReadAsync(filename);
            case PromotedTable t: return t.ReadAsync(filename);
            case RefusedTable t: return t.ReadAsync(filename);
            default: throw new ArgumentException("Not a table.");
        }
    }

    private static void Rows(object table, StringBuilder json)
    {
        IEnumerable rows;

        switch (table)
        {
            case EvolutionTable t: rows = t.Records; break;
            case PromotedTable t: rows = t.Records; break;
            case RefusedTable t: rows = t.Records; break;
            default: throw new ArgumentException("Not a table.");
        }

        json.Append('[');
        bool first = true;

        foreach (object row in rows)
        {
            if (!first)
                json.Append(',');

            json.Append(row.ToString());
            first = false;
        }

        json.Append(']');
    }

    /// <summary>
    /// Reads one table by name. The three table types are what both generations have in
    /// common; their fields are not, and nothing here looks at those.
    /// </summary>
    private static async Task<IEnumerable> Read(string table, string filename)
    {
        switch (table)
        {
            case "Evolution":
            {
                var t = new EvolutionTable();
                await t.ReadAsync(filename);
                return t.Records;
            }

            case "Promoted":
            {
                var t = new PromotedTable();
                await t.ReadAsync(filename);
                return t.Records;
            }

            case "Refused":
            {
                var t = new RefusedTable();
                await t.ReadAsync(filename);
                return t.Records;
            }

            default:
                throw new ArgumentException($"No table called `{table}` in this generation.");
        }
    }

    private static string Quote(string value)
    {
        var sb = new StringBuilder("\"");

        foreach (char c in value ?? "")
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < 0x20)
                        sb.Append("\\u").Append(((int)c).ToString("x4"));
                    else
                        sb.Append(c);
                    break;
            }
        }

        return sb.Append('"').ToString();
    }
}
