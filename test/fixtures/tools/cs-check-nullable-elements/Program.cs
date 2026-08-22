// Read-back check for an array whose elements may have no value.
//
// Compiles against the generated accessor, loads the .tcb the binary exporter wrote, and
// prints what it read as JSON on stdout. The harness compares that against the JSON the
// exporter wrote from the same sheet, so the two read paths have to agree.
//
// What a compile cannot answer is here: the file carries a bitmap of one bit per element
// written, the generated code walks it with a counter that steps once per element of every
// row, and whether those two walks are the same walk is only settled by reading.
//
// `words` is a `string?[]`, and it is in the output on purpose - an absent element and an
// empty string are the same value, so only the presence bit tells them apart.
//
// spec/nullable-array-elements.md.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

using Tabbit.Fixtures.NullableElements;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        if (args.Length < 1)
        {
            Console.Error.WriteLine("usage: cs-check-nullable-elements <binary-table-directory>");
            return 2;
        }

        try
        {
            await NullableElementsAccessor.ReadAllAsync(args[0]);

            var rows = NullableElementsAccessor.Listing.Records.Select(record => new Dictionary<string, object>
            {
                ["index"] = record.Index,

                // Null where the row says the element has none, which is what the JSON
                // exporter writes for the same cell.
                ["holes"] = record.Holes
                    .Select((value, at) => record.HasHolesAt(at) ? (object)value : null)
                    .ToList(),

                ["both"] = record.HasBoth
                    ? record.Both.Select((value, at) => record.HasBothAt(at) ? (object)value : null).ToList()
                    : null,

                ["words"] = record.Words
                    .Select((value, at) => record.HasWordsAt(at) ? value : null)
                    .ToList(),
            }).ToList();

            Console.WriteLine(JsonSerializer.Serialize(rows));
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }
}
