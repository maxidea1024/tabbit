// Round-trip check for lookups keyed by several columns at once.
//
// Compiles against the generated accessor, loads the .tcb files the binary exporter wrote,
// and prints what each lookup found. The harness compares that against what the sheet says.
//
// A compile is not enough here, and the reason is one row of the fixture. `Route` holds
// ("a b", "c") beside ("a", "b c"): under a key built by joining the columns with a
// separator those are one string, so one of the two rows is lost and the other answers for
// both. Nothing about that fails to compile, and nothing about it is visible in a golden -
// the generated text is the same either way. It shows up here, or it does not show up.
//
// spec/layout/primary-layout.md section 3.5.

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;

using Tabbit.Fixtures.CompositeKey;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        if (args.Length < 1)
        {
            Console.Error.WriteLine("usage: cs-check-composite-key <binary-table-directory>");
            return 2;
        }

        try
        {
            await CompositeKeyAccessor.ReadAllAsync(args[0]);

            var report = new Dictionary<string, object>
            {
                // The pair a separator alone would collide.
                ["spaced"] = Code(CompositeKeyAccessor.Route.FindByFromAndTo("a b", "c")),
                ["shifted"] = Code(CompositeKeyAccessor.Route.FindByFromAndTo("a", "b c")),

                // A combination neither row holds, though both of its halves appear.
                ["absent"] = Code(CompositeKeyAccessor.Route.FindByFromAndTo("a", "c")),

                // The single-column secondary key beside the composite primary one.
                ["secondary"] = CompositeKeyAccessor.Route.FindByCode("R3") is { } row
                    ? row.From + "->" + row.To
                    : "<none>",

                // An int and an enum together, where the same stage and the same slot each
                // appear on more than one row.
                ["loadout"] = CompositeKeyAccessor.Loadout.FindByStageAndSlot(2, Slot.Feet) is { } slot
                    ? slot.Label
                    : "<none>",

                // Three columns, so nothing here is right by only ever having been asked two.
                ["grid"] = CompositeKeyAccessor.Grid.FindByXAndYAndZ(0, 0, "roof") is { } cell
                    ? cell.Name
                    : "<none>",

                ["containsGrid"] = CompositeKeyAccessor.Grid.ContainsXAndYAndZ(1, 0, "floor"),
                ["containsAbsent"] = CompositeKeyAccessor.Grid.ContainsXAndYAndZ(9, 9, "floor"),

                // A key made of two references, which is where a composite one most often
                // comes from. Each argument is the target's key - a string for one target and
                // a number for the other - and this call is what says so: a lookup taking the
                // target's rows would not compile against these.
                // spec/references/reference-surface-naming.md sections 4 and 5.
                ["link"] = CompositeKeyAccessor.BeastMove.FindByBeastIdAndMoveId("deer", 2)
                    is { } pair
                    ? pair.Power.ToString()
                    : "<none>",

                ["linkAbsent"] = CompositeKeyAccessor.BeastMove
                    .FindByBeastIdAndMoveId("wolf", 2) is null,

                // And the row each half resolved to, which is the other name the column made.
                ["linkRow"] = CompositeKeyAccessor.BeastMove
                    .FindByBeastIdAndMoveId("deer", 1) is { } found
                    ? found.BeastByBeastId.Name + "/" + found.MoveByMoveId.Name
                    : "<none>",
            };

            Console.WriteLine(JsonSerializer.Serialize(
                report, new JsonSerializerOptions { WriteIndented = true }));

            return 0;
        }
        catch (Exception failure)
        {
            Console.Error.WriteLine(failure);
            return 1;
        }
    }

    private static string Code(RouteRecord row) => row is null ? "<none>" : row.Code;
}
