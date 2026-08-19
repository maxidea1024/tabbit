// Read-back check for an array of references - numbered reference columns folded into one.
//
// A compile cannot answer any of this. The read allocates three arrays per column and fills
// one of them; the linking pass walks the keys and writes the resolved values into another.
// Generated code that builds and resolves nothing - or resolves element 0 and leaves the rest
// - is exactly what this shape produced while nothing read it.
//
// Both forms of a reference, because they resolve to different types: `Slot` is the whole row
// and `Tier` is one of that row's values. The resolved value is printed rather than the key,
// so an element that resolved to the wrong row shows as a different word.
//
// spec/nullable-array-elements.md · spec/references-in-records.md.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

using Tabbit.Fixtures.SerialRef;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        if (args.Length < 1)
        {
            Console.Error.WriteLine("usage: cs-check-serial-ref <binary-table-directory>");
            return 2;
        }

        try
        {
            await SerialRefAccessor.ReadAllAsync(args[0]);

            var report = new Dictionary<string, object>
            {
                ["Kit"] = SerialRefAccessor.Kit.Records.Select(r => new Dictionary<string, object>
                {
                    ["index"] = r.Index,

                    // The length the file gave, so a read that sized the array from the page
                    // rather than the descriptor shows here first.
                    ["length"] = r.SlotArray.Length,

                    // Each element's key and what it resolved to, per element - resolving the
                    // first and leaving the rest is what a loop bounded by the wrong number
                    // produces.
                    ["slots"] = r.SlotArray.Select((piece, at) => new Dictionary<string, object>
                    {
                        ["key"] = r._slotArray_Piece_index[at],
                        ["resolved"] = r._slotArray_F[at] ? piece.Name : "<unresolved>",
                    }).ToList(),

                    // A field reference: the target's own value rather than its row, so the
                    // resolved member is an `int` and there is no name to print.
                    ["tiers"] = r.TierArray.Select((tier, at) => new Dictionary<string, object>
                    {
                        ["key"] = r._tierArray_Piece_index[at],
                        ["resolved"] = r._tierArray_F[at] ? tier.ToString() : "<unresolved>",
                    }).ToList(),
                }).ToList(),
            };

            Console.WriteLine(JsonSerializer.Serialize(report));
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }
}
