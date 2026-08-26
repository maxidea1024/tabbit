// Read-back check for a record whose member is itself a record.
//
// Compiles against the generated accessor, loads the .tcb the binary exporter wrote, and
// prints what it read as JSON on stdout. The harness compares that against the values the
// fixture's sheet holds.
//
// A separate harness from `cs-check` because that one names the `core` fixture's tables and
// cannot be pointed elsewhere. What it buys here is the one question a compile cannot answer:
// the declaration says `Star[j].Position.X` and the file says a fixed-array column called
// `Deep.Star.Position.X`, and whether those are the same column is only settled by reading.
//
// spec/types/nested-multi-level.md.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

using Tabbit.Fixtures.NestedDeep;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        if (args.Length < 1)
        {
            Console.Error.WriteLine("usage: cs-check-nested-deep <binary-table-directory>");
            return 2;
        }

        try
        {
            await NestedDeepAccessor.ReadAllAsync(args[0]);

            var rows = NestedDeepAccessor.Deep.Records.Select(record => new Dictionary<string, object>
            {
                ["index"] = record.Index,

                // The whole point: the value and the record at the same level, each element
                // of the outer array carrying both.
                ["star"] = record.Star.Select(star => new Dictionary<string, object>
                {
                    ["id"] = star.Id,
                    ["x"] = star.Position.X,
                    ["y"] = star.Position.Y,
                }).ToList(),
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
