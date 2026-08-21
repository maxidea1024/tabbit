// Read-back check for a column whose value is a row of one of several tables.
//
// Compiles against the generated accessor, loads the .tcb files the binary exporter wrote,
// and prints what it read as JSON on stdout.
//
// A compile is not enough, and the reason is the shape itself. The resolved row is stored in
// one slot whatever table it came from, and which table that was is a separate member the
// linking pass sets beside it. Nothing at compile time says the two agree: a discriminator
// set one target late still compiles, and every property then casts a row of the wrong table
// - which throws at the first read, not at build.
//
// So what this reports is the pair. For every column: the key, the discriminator, and the
// name off whichever target answered. A wrong discriminator shows as a name from the wrong
// catalogue, or as an exception.
//
// spec/multi-target-accessors.md.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

using Tabbit.Fixtures.MultiTarget;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        if (args.Length < 1)
        {
            Console.Error.WriteLine("usage: cs-check-multi-target <binary-table-directory>");
            return 2;
        }

        try
        {
            await MultiTargetAccessor.ReadAllAsync(args[0]);

            var report = new Dictionary<string, object>
            {
                ["Holder"] = MultiTargetAccessor.Holder.Records.Select(r => new Dictionary<string, object>
                {
                    ["index"] = r.Index,

                    // Two targets. The key first, because it is what the file carried and the
                    // rest is what the linking made of it.
                    ["pick"] = r.Pick,
                    ["pickTarget"] = r.PickTarget.ToString(),
                    ["pickName"] = PickName(r),

                    // Five targets, which is where an off-by-one in the discriminator has
                    // room to hide.
                    ["wide"] = r.Wide,
                    ["wideTarget"] = r.WideTarget.ToString(),
                    ["wideName"] = WideName(r),

                    // The same notation with one name, which must be an ordinary reference:
                    // a resolved row and no discriminator at all.
                    ["only"] = r.Only != null ? r.Only.Name : "<unresolved>",

                    // Optional, so one row says it points at none of them.
                    ["hasMaybe"] = r.HasMaybe,
                    ["maybeTarget"] = r.MaybeTarget.ToString(),
                    ["maybeName"] = MaybeName(r),
                }).ToList(),

                // The three record shapes, each with a member that reaches several tables.
                // What this catches is the element number: an array of records numbers the
                // group, a record of one numbers nothing, and a record of arrays numbers the
                // member - and a linking pass written around one of them resolves the wrong
                // element in the others. spec/references-in-records.md.
                ["Loadout"] = MultiTargetAccessor.Loadout.Records.Select(r => new Dictionary<string, object>
                {
                    ["index"] = r.Index,
                    ["slots"] = r.Slot.Select(slot => new Dictionary<string, object>
                    {
                        ["key"] = slot.Pick,
                        ["target"] = slot.Pick_target.ToString(),
                        ["name"] = slot.WeaponByPick != null ? "Weapon:" + slot.WeaponByPick.Name
                            : slot.ArmourByPick != null ? "Armour:" + slot.ArmourByPick.Name
                            : "<none>",
                    }).ToList(),
                }).ToList(),

                ["Fitting"] = MultiTargetAccessor.Fitting.Records.Select(r => new Dictionary<string, object>
                {
                    ["index"] = r.Index,
                    ["key"] = r.Main.Pick,
                    ["target"] = r.Main.Pick_target.ToString(),
                    ["name"] = r.Main.WeaponByPick != null ? "Weapon:" + r.Main.WeaponByPick.Name
                        : r.Main.ArmourByPick != null ? "Armour:" + r.Main.ArmourByPick.Name
                        : "<none>",
                }).ToList(),

                ["Rack"] = MultiTargetAccessor.Rack.Records.Select(r => new Dictionary<string, object>
                {
                    ["index"] = r.Index,
                    ["picks"] = Enumerable.Range(0, r.Slots.Pick.Length).Select(at => new Dictionary<string, object>
                    {
                        ["key"] = r.Slots.Pick[at],
                        ["target"] = r.Slots.Pick_target[at].ToString(),
                        ["name"] = r.Slots.WeaponByPick(at) != null ? "Weapon:" + r.Slots.WeaponByPick(at).Name
                            : r.Slots.ArmourByPick(at) != null ? "Armour:" + r.Slots.ArmourByPick(at).Name
                            : "<none>",
                    }).ToList(),
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

    // Every property is read, not just the one the discriminator names. That is the check: the
    // others have to be null, because a slot filled with one table's row must not be handed
    // out as another's.
    private static string PickName(HolderTable.Record r)
    {
        var names = new List<string>();

        if (r.WeaponByPick != null)
            names.Add("Weapon:" + r.WeaponByPick.Name);

        if (r.ArmourByPick != null)
            names.Add("Armour:" + r.ArmourByPick.Name);

        return names.Count == 0 ? "<none>" : string.Join("+", names);
    }

    private static string WideName(HolderTable.Record r)
    {
        var names = new List<string>();

        if (r.WeaponByWide != null)
            names.Add("Weapon:" + r.WeaponByWide.Name);

        if (r.ArmourByWide != null)
            names.Add("Armour:" + r.ArmourByWide.Name);

        if (r.TrinketByWide != null)
            names.Add("Trinket:" + r.TrinketByWide.Name);

        if (r.MountByWide != null)
            names.Add("Mount:" + r.MountByWide.Name);

        if (r.BannerByWide != null)
            names.Add("Banner:" + r.BannerByWide.Name);

        return names.Count == 0 ? "<none>" : string.Join("+", names);
    }

    private static string MaybeName(HolderTable.Record r)
    {
        var names = new List<string>();

        if (r.WeaponByMaybe != null)
            names.Add("Weapon:" + r.WeaponByMaybe.Name);

        if (r.ArmourByMaybe != null)
            names.Add("Armour:" + r.ArmourByMaybe.Name);

        return names.Count == 0 ? "<none>" : string.Join("+", names);
    }
}
