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
// spec/types/nullable-array-elements.md · spec/references/references-in-records.md.

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
                    ["length"] = r.Slot.Length,

                    // Each element's key and what it resolved to, per element - resolving the
                    // first and leaving the rest is what a loop bounded by the wrong number
                    // produces. The column's name is the key's and the rows are under the
                    // derived one. spec/references/reference-surface-naming.md sections 4 and 5.
                    ["slots"] = r.PieceBySlot.Select((piece, at) => new Dictionary<string, object>
                    {
                        ["key"] = r.Slot[at],
                        ["resolved"] = r._slot_F[at] ? piece.Name : "<unresolved>",
                    }).ToList(),

                    // A field reference: the target's own value rather than its row, so the
                    // resolved member is an `int` and there is no name to print.
                    ["tiers"] = r.Tier.Select((tier, at) => new Dictionary<string, object>
                    {
                        ["key"] = r._tier_Piece_index[at],
                        ["resolved"] = r._tier_F[at] ? tier.ToString() : "<unresolved>",
                    }).ToList(),
                }).ToList(),

                // The same shape with the length trimmed to each row's. What this catches
                // that the table above cannot: the key array is allocated per row here, and
                // the read left it empty - so the first element written into it was an index
                // out of range. spec/types/variable-length-record-arrays.md.
                ["TrimKit"] = SerialRefAccessor.TrimKit.Records.Select(r => new Dictionary<string, object>
                {
                    ["index"] = r.Index,
                    ["length"] = r.Slot.Length,
                    ["keyLength"] = r.Slot.Length,

                    ["slots"] = r.BitBySlot.Select((bit, at) => new Dictionary<string, object>
                    {
                        ["key"] = r.Slot[at],
                        ["resolved"] = r._slot_F[at] ? bit.Name : "<unresolved>",
                    }).ToList(),

                    ["tiers"] = r.Tier.Select((tier, at) => new Dictionary<string, object>
                    {
                        ["key"] = r._tier_Bit_index[at],
                        ["resolved"] = r._tier_F[at] ? tier.ToString() : "<unresolved>",
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
