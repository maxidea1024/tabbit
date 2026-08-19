using System;
using System.IO;
using Xunit;

namespace Tabbit.Tests;

/// <summary>
/// That the Lua generator's record groups, optional columns and key types read back what
/// the sheet said.
/// </summary>
/// <remarks>
/// Lua parses even less than Python: `record.slot[j].id = x` loads whatever `slot` is,
/// and there is no compile step at all. So every gate here reads the binary the same
/// conversion wrote and asserts the values, which is the only static-language gate a
/// dynamic target can be given. Arrays are 1-based - the sheet's `name[0]` is
/// `row.name[1]` - so every subscript below is one up from its Python twin.
/// spec/lua-language-support.md.
/// </remarks>
public class LuaNestedAndOptionalTests
{
    /// <summary>
    /// An array whose elements may be absent: the per-element answer beside the value.
    /// </summary>
    [Fact]
    public void Optional_array_elements_read()
        => AssertReads("nullable-elements", @"
local t = require('tables').new()
t:readAll(arg[1])
local rows = t.listing.records
assert(#rows == 5, #rows)
assert(eq(rows[2].hasHolesAt, {true, false, true}))
assert(eq(rows[4].hasWordsAt, {true, true, true}))
assert(eq(rows[4].words, {'a', '', 'c'}))
");

    /// <summary>
    /// A record, an array of records whose members are of different types, and a scalar
    /// serial field beside them.
    /// </summary>
    [Fact]
    public void A_record_group_reads()
        => AssertReads("nested", @"
local t = require('tables').new()
t:readAll(arg[1])
local rows = t.loadout.records
assert(#rows == 3, #rows)
for i = 1, 3 do assert(#rows[i].slot == 2, #rows[i].slot) end
assert(rows[1].pos.x == 1.5 and rows[1].pos.y == -2.5)
assert(rows[1].slot[1].id == 10 and rows[1].slot[1].label == 'sword')
assert(rows[1].slot[2].id == 11 and rows[1].slot[2].label == 'shield')
assert(rows[3].slot[1].label == '')
assert(eq(rows[1].tagArray, {'a', 'b'}))
");

    /// <summary>
    /// A record array whose length is each row's - including the row that filled in none
    /// of it, and the one whose gap is a value rather than an end.
    /// </summary>
    [Fact]
    public void A_trimmed_record_array_reads()
        => AssertReads("record-trim", @"
local t = require('tables').new()
t:readAll(arg[1])
local rows = t.loot.records
local lengths = {}
for i = 1, #rows do lengths[i] = #rows[i].slot end
assert(eq(lengths, {3, 2, 3, 0, 2}), table.concat(lengths, ','))
assert(rows[3].slot[2].id == 0 and rows[3].slot[2].count == 0)
assert(#rows[4].slot == 0)
assert(rows[1].pos.x == 5 and rows[1].pos.y == 6)
");

    /// <summary>
    /// A record whose members are arrays - one record, each member holding all of its
    /// elements, out of the same columns an array of records would use.
    /// spec/nested-multi-level.md.
    /// </summary>
    [Fact]
    public void A_record_of_arrays_reads()
        => AssertReads("member-array", @"
local t = require('tables').new()
t:readAll(arg[1])
local rows = t.guide.records
assert(eq(rows[1].skill.step, {10, 11}))
assert(eq(rows[1].skill.order, {'a', 'b'}))
assert(eq(rows[2].skill.step, {20, 21}))
assert(rows[1].pos.x == 1.5)
assert(eq(rows[1].tagArray, {'t1', 't2'}))
assert(eq(rows[1].grid[1], {1, 2, 3}) and eq(rows[1].grid[2], {4, 5, 6}))
assert(eq(rows[2].grid[1], {7, 8, 9}) and eq(rows[2].grid[2], {10, 11, 12}))
");

    /// <summary>
    /// A record whose member is itself a record - a value and a record at the same
    /// level, read out of the binary. spec/nested-multi-level.md.
    /// </summary>
    [Fact]
    public void A_record_inside_a_record_reads()
        => AssertReads("nested-deep", @"
local t = require('tables').new()
t:readAll(arg[1])
local rows = t.deep.records
assert(#rows == 2)
assert(#rows[1].star == 2 and #rows[2].star == 2)
assert(rows[1].star[1].id == 10)
assert(rows[1].star[1].position.x == 11 and rows[1].star[1].position.y == 12)
assert(rows[1].star[2].position.y == 22)
assert(rows[2].star[1].position.x == 31)
assert(rows[2].star[2].id == 40 and rows[2].star[2].position.y == 42)
");

    /// <summary>
    /// A row that has a value and two that do not, for every type that can be optional.
    /// </summary>
    /// <remarks>
    /// `label` and `hidden` are the two that matter most: a blank string and a blank
    /// bool have always read as `''` and `false`, so only the presence flag tells those
    /// rows apart from the ones that wrote those values.
    /// </remarks>
    [Fact]
    public void Optional_columns_read()
        => AssertReads("optional", @"
local t = require('tables').new()
t:readAll(arg[1])
local rows = t.drop.records
assert(#rows == 3)
assert(rows[1].hasBonus and rows[1].bonus == 5)
assert(rows[1].hasCosts and eq(rows[1].costs, {10, 20}))
assert(rows[1].hasLabel and rows[1].label == 'first')
assert(rows[1].hasHidden and rows[1].hidden == true)
for i = 2, 3 do
  assert(not rows[i].hasBonus and rows[i].bonus == 0)
  assert(not rows[i].hasCosts and #rows[i].costs == 0)
  assert(not rows[i].hasLabel and rows[i].label == '')
  assert(not rows[i].hasHidden and rows[i].hidden == false)
end
");

    /// <summary>
    /// A record whose member references another table: the row it resolved to beside
    /// the key that came off the wire, and a linking pass that walks the elements.
    /// spec/references-in-records.md.
    /// </summary>
    [Fact]
    public void A_reference_inside_a_record_reads()
        => AssertReads("record-ref", @"
local t = require('tables').new()
t:readAll(arg[1])
local rows = t.loadout.records
assert(rows[1].slot[1].itemId.name == 'sword')
assert(rows[1].slot[2].itemId.name == 'shield')
assert(rows[1].slot[1].swapId.name == 'shield')
assert(rows[2].slot[2].itemId == nil)
assert(t.holder.records[1].main.itemId.name == 'shield')
assert(t.bag.records[1].slots.itemId[1].name == 'sword')
assert(t.mount.records[1].rig[1].core.itemId.name == 'sword')
assert(t.pose.records[1].step[1].clipId.index == 'Idle_01')
local parts = {}
for i = 1, #t.kit.records do parts[i] = #t.kit.records[i].part end
assert(eq(parts, {3, 2, 0}), table.concat(parts, ','))
assert(t.kit.records[2].part[1].itemId.name == 'shield')
");

    /// <summary>
    /// An array of references: numbered reference columns folded into one array.
    /// </summary>
    /// <remarks>
    /// The resolved list is asked element by element, and its length is the key list's:
    /// a Lua table cannot hold nil, so an unresolved element is an absent entry and `#`
    /// over the value list stops at the first hole. The keys are what say how many
    /// elements the row carried. spec/lua-language-support.md.
    /// </remarks>
    [Fact]
    public void An_array_of_references_reads()
        => AssertReads("serial-ref", @"
local t = require('tables').new()
t:readAll(arg[1])
local rows = t.kit.records
for i = 1, 3 do assert(#rows[i].slotArrayIndex == 2) end
assert(rows[1].slotArray[1].name == 'sword')
assert(rows[1].slotArray[2].name == 'shield')
assert(rows[2].slotArray[1].name == 'ring')
assert(rows[3].slotArray[2] == nil)
assert(rows[1].tierArray[1] == 3 and rows[1].tierArray[2] == 5)
assert(rows[3].tierArray[2] == nil)
");

    /// <summary>
    /// Lookups by every key type - most of all the int64 one, whose map is keyed by the
    /// decimal string because an int64 is FFI cdata under LuaJIT and cdata table keys
    /// compare by identity. The corpus keys sit past 2^53, so a lookup that rounded
    /// through a double misses. spec/lua-language-support.md.
    /// </summary>
    [Fact]
    public void Keys_of_every_type_look_up()
        => AssertReads("key-types", @"
local tcb = require('tabbit.tcb_reader')
local t = require('tables').new()
t:readAll(arg[1])
assert(#t.ledger.records == 3)
local past = t.ledger:findByIndex(tcb.int64FromString('9007199254740993'))
assert(past ~= nil and past.amount == 10)
local below = t.ledger:findByIndex(tcb.int64FromString('-9007199254740993'))
assert(below ~= nil and below.amount == -10)
assert(t.ledger:findByIndex(1).amount == 0)
assert(t.asset:findByIndex('6f9619ff-8b86-d011-b42d-00c04fc964ff').slot == 2)
assert(t.asset:findByIndex('no-such-key') == nil)
assert(t.slotting:getByIndexOrThrow(2).capacity == 3)
");

    private static void AssertReads(string scenario, string body)
    {
        var conversion = TabbitRunner.Convert(scenario);
        Assert.True(conversion.Succeeded,
            $"Conversion of `{scenario}` failed.{Environment.NewLine}{conversion.Describe()}");

        // A hard failure rather than a skip, as with the other toolchain gates: a gate
        // that quietly turns itself off is worse than no gate.
        Assert.True(ConformanceHarness.LuaIsAvailable(out string why),
            $"A C toolchain is required to build the Lua host. {why}");

        string binaryDir = Path.Combine(RepoLayout.OutputDir(scenario), "binary");

        // The list comparison every snippet leans on, prepended so each stays a list of
        // assertions.
        const string helpers = @"
local function eq(a, b)
  if #a ~= #b then return false end
  for i = 1, #a do if a[i] ~= b[i] then return false end end
  return true
end
_G.eq = eq
";

        var result = ConformanceHarness.RunLuaSnippet(scenario, helpers + body, binaryDir);

        Assert.True(result.Succeeded,
            $"Reading `{scenario}` through the generated Lua failed.{Environment.NewLine}{result.Output}");
    }
}
