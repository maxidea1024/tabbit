// Round-trip check for the generated C# and its emitted binary reader.
//
// Compiles against the generated accessor, loads the .tcb files the binary exporter
// wrote, and prints what it read as JSON on stdout. The harness compares that against
// the JSON exporter's output for the same workbook.
//
// This is the check C# went without for a long time, and its absence is why nothing
// noticed that the writer truncated every 64-bit value: the reader and writer were two
// halves of one runtime, so a value that survived the round trip inside C# looked fine
// whatever it did on the wire. Now the writer lives in the exporter and the reader is
// emitted separately, which makes them genuinely independent - and worth comparing.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

using Tabbit.Fixtures.Core;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        if (args.Length < 1)
        {
            Console.Error.WriteLine("usage: cs-check <binary-table-directory>");
            return 2;
        }

        try
        {
            await CoreAccessor.ReadAllAsync(args[0]);

            var report = new Dictionary<string, object>
            {
                ["TestFieldTypes"] = CoreAccessor.TestFieldTypes.Records.Select(r => new Dictionary<string, object>
                {
                    ["index"] = r.Index,
                    ["stringField"] = r.StringField,
                    ["boolField"] = r.BoolField,
                    ["intField"] = r.IntField,
                    // The value the old writer used to mangle.
                    ["bigIntField"] = r.BigIntField.ToString(CultureInfo.InvariantCulture),
                    ["uuidField"] = r.UuidField.ToString(),
                    ["datetimeField"] = r.DatetimeField.ToString("o", CultureInfo.InvariantCulture),
                    ["timespanField"] = r.TimespanField.ToString(),
                    ["valueTypeField"] = (int)r.ValueTypeField,
                }).ToList(),

                // Both array kinds, which are encoded differently: a delimited array
                // carries its length, a serial field's is a constant.
                ["ArrayTypes"] = CoreAccessor.ArrayTypes.Records.Select(r => new Dictionary<string, object>
                {
                    ["index"] = r.Index,
                    ["tags"] = r.Tags,
                    ["costs"] = r.Costs,
                    ["grades"] = r.Grades.Select(g => (int)g).ToArray(),
                    ["slot"] = r.Slot,
                }).ToList(),

                // References, resolved to records once every table is loaded.
                ["Item"] = CoreAccessor.Item.Records.Select(r => new Dictionary<string, object>
                {
                    ["index"] = r.Index,
                    ["name"] = r.Name,
                    ["categoryName"] = r.CategoryId != null ? r.CategoryId.Name : "<unresolved>",
                }).ToList(),
            };

            Console.OutputEncoding = Encoding.UTF8;
            Console.WriteLine(JsonSerializer.Serialize(report, new JsonSerializerOptions
            {
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            }));

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"failed: {ex}");
            return 1;
        }
    }
}
