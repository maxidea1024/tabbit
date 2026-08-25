// Round-trip check for a reference that is a member of a record group.
//
// A compile is not enough here. The read writes into the element's own key, the linking
// pass walks the array and resolves each one, and both were missing - so the failure this
// guards against is generated code that builds and leaves every element unresolved. That
// is exactly what the refusal's comment warned about before it was lifted.
//
// All three record shapes, because each puts the element number somewhere else: an array
// of records indexes the group, a record of one indexes nothing, and a record of arrays
// indexes the member. The first of them worked while the other two did not compile.
//
// spec/references-in-records.md.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

using Tabbit.Fixtures.RecordRef;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        if (args.Length < 1)
        {
            Console.Error.WriteLine("usage: cs-check-record-ref <binary-table-directory>");
            return 2;
        }

        try
        {
            await RecordRefAccessor.ReadAllAsync(args[0]);

            var report = new Dictionary<string, object>
            {
                ["Loadout"] = RecordRefAccessor.Loadout.Records.Select(r => new Dictionary<string, object>
                {
                    ["index"] = r.Index,

                    // Each element's own key and each element's own resolved row. Both, and
                    // per element, because resolving element 0 and leaving element 1 alone
                    // is the shape a loop that forgets its index produces.
                    // Two references in one element, at the same table. A key named after the
                    // group and the target would be one name for both, so the second would
                    // land in the first one's - which is why the key lives in the element.
                    ["slots"] = r.Slot.Select(s => new Dictionary<string, object>
                    {
                        ["key"] = s.ItemId,
                        ["resolved"] = s.ItemId_F ? s.ItemByItemId.Name : "<unresolved>",
                        ["swapKey"] = s.SwapId,
                        ["swap"] = s.SwapId_F ? s.ItemBySwapId.Name : "<unresolved>",
                        ["count"] = s.Count,
                    }).ToList(),
                }).ToList(),

                // A record of one: no element number anywhere, so the linking pass has no
                // loop at all. Written around `[j]` it did not compile.
                ["Holder"] = RecordRefAccessor.Holder.Records.Select(r => new Dictionary<string, object>
                {
                    ["index"] = r.Index,
                    ["key"] = r.Main.ItemId,
                    ["resolved"] = r.Main.ItemId_F ? r.Main.ItemByItemId.Name : "<unresolved>",
                    ["count"] = r.Main.Count,
                }).ToList(),

                // A record of arrays: the number is on the member, so the key and the flag
                // are arrays beside the rows they belong to rather than single values.
                ["Bag"] = RecordRefAccessor.Bag.Records.Select(r => new Dictionary<string, object>
                {
                    ["index"] = r.Index,
                    ["slots"] = Enumerable.Range(0, r.Slots.ItemByItemId.Length)
                                          .Select(k => new Dictionary<string, object>
                                          {
                                              ["key"] = r.Slots.ItemId[k],
                                              ["resolved"] = r.Slots.ItemId_F[k]
                                                  ? r.Slots.ItemByItemId[k].Name
                                                  : "<unresolved>",
                                              ["count"] = r.Slots.Count[k],
                                          }).ToList(),
                }).ToList(),

                // Two levels in: the member is named by its whole path, so a generator that
                // took only the last part of it writes the key somewhere nothing declared.
                ["Mount"] = RecordRefAccessor.Mount.Records.Select(r => new Dictionary<string, object>
                {
                    ["index"] = r.Index,
                    ["rigs"] = r.Rig.Select(g => new Dictionary<string, object>
                    {
                        ["key"] = g.Core.ItemId,
                        ["resolved"] = g.Core.ItemId_F ? g.Core.ItemByItemId.Name : "<unresolved>",
                        ["count"] = g.Core.Count,
                    }).ToList(),
                }).ToList(),

                // A key that is not a number. Declared, read and compared against "points at
                // nothing" in three separate places, none of which may assume `int`.
                ["Pose"] = RecordRefAccessor.Pose.Records.Select(r => new Dictionary<string, object>
                {
                    ["index"] = r.Index,
                    ["steps"] = r.Step.Select(s => new Dictionary<string, object>
                    {
                        ["key"] = s.ClipId,
                        ["resolved"] = s.ClipId_F ? s.ClipByClipId.Index : "<unresolved>",
                        ["weight"] = s.Weight,
                    }).ToList(),
                }).ToList(),

                // A trimmed group: the elements are this row's rather than the sheet's, and
                // the key was put inside the element so it would be allocated with them.
                ["Kit"] = RecordRefAccessor.Kit.Records.Select(r => new Dictionary<string, object>
                {
                    ["index"] = r.Index,
                    ["length"] = r.Part.Length,
                    ["parts"] = r.Part.Select(p => new Dictionary<string, object>
                    {
                        ["key"] = p.ItemId,
                        ["resolved"] = p.ItemId_F ? p.ItemByItemId.Name : "<unresolved>",
                        ["count"] = p.Count,
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
}
