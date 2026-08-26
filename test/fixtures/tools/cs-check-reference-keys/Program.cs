// Round-trip check for references whose target is keyed by something other than an `int`.
//
// Compiles against the generated accessor, loads the .tcb files the binary exporter wrote,
// and prints what it read as JSON on stdout. The harness compares that against the JSON
// exporter's output for the same workbook.
//
// A compile is not enough here, and that is the whole reason this exists. The element a
// reference column declares and the element the writer emits are decided in two different
// places, and while a key could only be an int they agreed by accident. Making the writer
// emit the key's own element left three readers still checking for `ElementI32` - a file
// this build wrote, refused by the code this build generated, with nothing to see at compile
// time.
//
// spec/references/reference-key-types.md.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

using Tabbit.Fixtures.ReferenceKeys;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        if (args.Length < 1)
        {
            Console.Error.WriteLine("usage: cs-check-reference-keys <binary-table-directory>");
            return 2;
        }

        try
        {
            await ReferenceKeysAccessor.ReadAllAsync(args[0]);

            var report = new Dictionary<string, object>
            {
                // The stored keys, as they came off the wire. Reported beside the resolved
                // rows because the two answer different questions: whether the key survived
                // the round trip, and whether it found the row it names.
                ["Clip"] = ReferenceKeysAccessor.Clip.Records.Select(r => new Dictionary<string, object>
                {
                    ["index"] = r.Index,

                    ["animKey"] = r._anim_Animation_index,
                    ["animBlend"] = r.AnimationByAnim != null ? (object)r.AnimationByAnim.Blend : "<unresolved>",

                    // As text, so a 64-bit key is compared exactly rather than through a
                    // double - which is the same reason the JSON exporter writes it as one.
                    ["entryKey"] = r._entry_Ledger_index.ToString(),
                    ["entryNote"] = r.LedgerByEntry != null ? r.LedgerByEntry.Note : "<unresolved>",

                    ["coverKey"] = r._cover_Art_index.ToString(),
                    ["coverPath"] = r.ArtByCover != null ? r.ArtByCover.Path : "<unresolved>",
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
