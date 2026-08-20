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

        if (r.PickAsWeapon != null)
            names.Add("Weapon:" + r.PickAsWeapon.Name);

        if (r.PickAsArmour != null)
            names.Add("Armour:" + r.PickAsArmour.Name);

        return names.Count == 0 ? "<none>" : string.Join("+", names);
    }

    private static string WideName(HolderTable.Record r)
    {
        var names = new List<string>();

        if (r.WideAsWeapon != null)
            names.Add("Weapon:" + r.WideAsWeapon.Name);

        if (r.WideAsArmour != null)
            names.Add("Armour:" + r.WideAsArmour.Name);

        if (r.WideAsTrinket != null)
            names.Add("Trinket:" + r.WideAsTrinket.Name);

        if (r.WideAsMount != null)
            names.Add("Mount:" + r.WideAsMount.Name);

        if (r.WideAsBanner != null)
            names.Add("Banner:" + r.WideAsBanner.Name);

        return names.Count == 0 ? "<none>" : string.Join("+", names);
    }

    private static string MaybeName(HolderTable.Record r)
    {
        var names = new List<string>();

        if (r.MaybeAsWeapon != null)
            names.Add("Weapon:" + r.MaybeAsWeapon.Name);

        if (r.MaybeAsArmour != null)
            names.Add("Armour:" + r.MaybeAsArmour.Name);

        return names.Count == 0 ? "<none>" : string.Join("+", names);
    }
}
