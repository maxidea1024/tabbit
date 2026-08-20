using System;
using System.IO;
using Xunit;

namespace Tabbit.Tests;

/// <summary>
/// That the PHP generator's record groups and optional columns parse, and that reading a file
/// through them gives back what the sheet said.
/// </summary>
/// <remarks>
/// `php -l` is a parse, and a parse says nothing about whether a class the generated code
/// names exists - which is exactly what a record group adds. So these lint every emitted file
/// and then read the binary the same conversion wrote, asserting the shapes only these two
/// features produce: the length of a trimmed record array, and a row that has no value for a
/// column that carries one.
///
/// The expected values are the fixture's, and the `nested`, `record-trim` and `optional`
/// goldens hold the JSON they are read from.
/// </remarks>
public class PhpNestedAndOptionalTests
{
    /// <summary>
    /// A record, an array of records whose members are of different types, and a scalar
    /// serial field beside them.
    /// </summary>
    [Fact]
    public void A_record_group_reads()
        => AssertReads("nested", "NestedAccessor", @"
$rows = $accessor->loadout->records;
assert(count($rows) === 3);
assert(array_map(fn($r) => count($r->slot), $rows) === [2, 2, 2]);
assert($rows[0]->pos->x === 1.5 && $rows[0]->pos->y === -2.5);
assert($rows[0]->slot[0]->id === 10 && $rows[0]->slot[0]->label === 'sword');
assert($rows[0]->slot[1]->id === 11 && $rows[0]->slot[1]->label === 'shield');
assert($rows[2]->slot[0]->label === '');
assert($rows[0]->tagArray === ['a', 'b']);
");

    /// <summary>
    /// An array whose elements may be absent: the per-element answer beside the value.
    /// </summary>
    /// <remarks>
    /// Read rather than compiled, which is what this gate is for: the bitmap is walked with a
    /// counter that steps once per element of every row, and a reader that stepped per row
    /// would still run. spec/nullable-array-elements.md.
    /// </remarks>
    [Fact]
    public void Optional_array_elements_read()
        => AssertReads("nullable-elements", "NullableElementsAccessor", @"
$rows = $accessor->listing->records;
assert(count($rows) === 5);
assert($rows[1]->hasHolesAt === [true, false, true]);
assert($rows[3]->words === ['a', '', 'c']);
assert($rows[3]->hasWordsAt === [true, true, true]);
");

    /// <summary>
    /// A record array whose length is each row's - including the row that filled in none of
    /// it, and the one whose gap is a value rather than an end.
    /// </summary>
    [Fact]
    public void A_trimmed_record_array_reads()
        => AssertReads("record-trim", "RecordTrimAccessor", @"
$rows = $accessor->loot->records;
assert(array_map(fn($r) => count($r->slot), $rows) === [3, 2, 3, 0, 2]);
assert($rows[2]->slot[1]->id === 0 && $rows[2]->slot[1]->count === 0);
assert($rows[3]->slot === []);
assert($rows[0]->pos->x === 5 && $rows[0]->pos->y === 6);
");

    /// <summary>
    /// A record whose members are arrays - one record, and each member holding all of its
    /// elements, out of the same columns an array of records would use.
    /// spec/nested-multi-level.md.
    /// </summary>
    [Fact]
    public void A_record_of_arrays_reads()
        => AssertReads("member-array", "MemberArrayAccessor", @"
$rows = $accessor->guide->records;
assert($rows[0]->skill->step === [10, 11]);
assert($rows[0]->skill->order === ['a', 'b']);
assert($rows[1]->skill->step === [20, 21]);
assert($rows[0]->pos->x === 1.5);
assert($rows[0]->tagArray === ['t1', 't2']);
assert($rows[0]->grid === [[1, 2, 3], [4, 5, 6]]);
assert($rows[1]->grid === [[7, 8, 9], [10, 11, 12]]);
");

    /// <summary>
    /// A record whose member is itself a record - a value and a record at the same level, read
    /// out of the binary. spec/nested-multi-level.md.
    /// </summary>
    /// <remarks>
    /// Reading rather than linting, for the reason this whole class exists:
    /// `$r->star[$k]->position->x` parses whatever `position` turns out to be, so only the values
    /// say the read reached the right column. It also settles whether the nested class was ever
    /// constructed - an unset typed property is an error to read, not a null.
    /// </remarks>
    [Fact]
    public void A_record_inside_a_record_reads()
        => AssertReads("nested-deep", "NestedDeepAccessor", @"
$rows = $accessor->deep->records;
assert(count($rows) === 2);
assert(count($rows[0]->star) === 2);
assert($rows[0]->star[0]->id === 10);
assert($rows[0]->star[0]->position->x === 11);
assert($rows[0]->star[0]->position->y === 12);
assert($rows[0]->star[1]->position->y === 22);
assert($rows[1]->star[0]->position->x === 31);
assert($rows[1]->star[1]->id === 40);
assert($rows[1]->star[1]->position->y === 42);
");

    /// <summary>
    /// A row that has a value and two that do not, for every type that can be optional.
    /// </summary>
    /// <remarks>
    /// `label` and `hidden` are the two that matter most: a blank string and a blank bool
    /// have always read as `''` and `false`, so only the presence flag tells those rows apart
    /// from the ones that wrote those values.
    /// </remarks>
    [Fact]
    public void Optional_columns_read()
        => AssertReads("optional", "OptionalAccessor", @"
$rows = $accessor->drop->records;
assert(count($rows) === 3);
assert($rows[0]->hasBonus && $rows[0]->bonus === 5);
assert($rows[0]->hasCosts && $rows[0]->costs === [10, 20]);
assert($rows[0]->hasLabel && $rows[0]->label === 'first');
assert($rows[0]->hasHidden && $rows[0]->hidden === true);
foreach ([$rows[1], $rows[2]] as $row) {
    assert(!$row->hasBonus && $row->bonus === 0);
    assert(!$row->hasCosts && $row->costs === []);
    assert(!$row->hasLabel && $row->label === '');
    assert(!$row->hasHidden && $row->hidden === false);
}
");


    /// <summary>
    /// A record whose member references another table: the row it resolved to beside the key
    /// that came off the wire, and a linking pass that walks the elements.
    /// </summary>
    /// <remarks>
    /// Reading rather than only compiling, because the failure this guards against is code
    /// that runs and leaves every element unresolved. Element 0 and element 1 point at
    /// different rows, so a loop that resolved the first and left the rest shows as the wrong
    /// value. spec/references-in-records.md.
    /// </remarks>
    [Fact]
    public void A_reference_inside_a_record_reads()
        => AssertReads("record-ref", "RecordRefAccessor", @"
$rows = $accessor->loadout->records;
assert($rows[0]->slot[0]->itemId->name === 'sword');
assert($rows[0]->slot[1]->itemId->name === 'shield');
assert($rows[0]->slot[0]->swapId->name === 'shield');
assert($rows[1]->slot[1]->itemId === null);
assert($accessor->holder->records[0]->main->itemId->name === 'shield');
assert($accessor->bag->records[0]->slots->itemId[0]->name === 'sword');
assert($accessor->mount->records[0]->rig[0]->core->itemId->name === 'sword');
assert($accessor->pose->records[0]->step[0]->clipId->index === 'Idle_01');
assert(array_map(fn($r) => count($r->part), $accessor->kit->records) === [3, 2, 0]);
assert($accessor->kit->records[1]->part[0]->itemId->name === 'shield');
");

    /// <summary>
    /// An array of references: numbered reference columns folded into one array.
    /// </summary>
    /// <remarks>
    /// Reading rather than only compiling, because the failure this guards against is code that
    /// runs and resolves nothing: the keys arrive on the wire and the values are written by the
    /// linking pass, which walks the array it was given. Element 0 and element 1 point at
    /// different rows, so a loop that resolved the first and left the rest shows as the wrong
    /// value. spec/nullable-array-elements.md.
    /// </remarks>
    [Fact]
    public void An_array_of_references_reads()
        => AssertReads("serial-ref", "SerialRefAccessor", @"
$rows = $accessor->kit->records;
assert(array_map(fn($r) => count($r->slotArray), $rows) === [2, 2, 2]);
assert($rows[0]->slotArray[0]->name === 'sword');
assert($rows[0]->slotArray[1]->name === 'shield');
assert($rows[1]->slotArray[0]->name === 'ring');
assert($rows[2]->slotArray[1] === null);
assert($rows[0]->tierArray === [3, 5]);
assert($rows[2]->tierArray[1] === null);
");

    /// <summary>
    /// A column whose value is a row of one of several tables.
    /// </summary>
    /// <remarks>
    /// Reading rather than only parsing, because what can go wrong here runs: the slot and the
    /// discriminator are written by the same assignment, and one set a target late hands out a
    /// row of the wrong table. The wide column is checked at its first and last target, which
    /// is where an off-by-one shows. spec/multi-target-accessors.md.
    /// </remarks>
    [Fact]
    public void A_multi_target_column_reads()
        => AssertReads("multi-target", "MultiTargetAccessor", @"
$rows = $accessor->holder->records;
if ($rows[0]->pickAsWeapon()->name !== 'weapon-a') { throw new \Exception('pick 0'); }
if ($rows[0]->pickAsArmour() !== null) { throw new \Exception('pick 0 armour'); }
if ($rows[1]->pickAsArmour()->name !== 'armour-b') { throw new \Exception('pick 1'); }
if ($rows[1]->pickAsWeapon() !== null) { throw new \Exception('pick 1 weapon'); }
if ($rows[0]->wideAsTrinket()->name !== 'trinket-a') { throw new \Exception('wide 0'); }
if ($rows[1]->wideAsBanner()->name !== 'banner-b') { throw new \Exception('wide 1'); }
if ($rows[2]->wideAsWeapon()->name !== 'weapon-a') { throw new \Exception('wide 2'); }
if ($rows[1]->maybeTarget->value !== 0) { throw new \Exception('maybe 1'); }
if ($rows[0]->only->name !== 'weapon-a') { throw new \Exception('only 0'); }
");

    private static void AssertReads(string scenario, string accessor, string body)
    {
        var conversion = TabbitRunner.Convert(scenario);
        Assert.True(conversion.Succeeded,
            $"Conversion of `{scenario}` failed.{Environment.NewLine}{conversion.Describe()}");

        // A hard failure rather than a skip, as with the other toolchain gates: a gate that
        // quietly turns itself off is worse than no gate.
        Assert.True(ConformanceHarness.PhpIsAvailable(out string why),
            $"A PHP interpreter is required to check the generated code. {why}");

        var linted = ConformanceHarness.CompilePhp(scenario, accessor);
        Assert.True(linted.Succeeded,
            $"The generated PHP for `{scenario}` does not parse.{Environment.NewLine}{linted.Output}");

        string binaryDir = Path.Combine(RepoLayout.OutputDir(scenario), "binary");
        string ns = "Tabbit\\Fixtures\\" + accessor.Replace("Accessor", "");

        // zend.assertions=1 makes `assert` evaluate; the default for a non-development ini
        // is to compile it away, which would make every one of these pass by not running.
        var result = ConformanceHarness.RunPhpSnippet(
            scenario,
            $"require_once __DIR__ . '/{accessor}.php'; "
            + $"$accessor = new \\{ns}\\{accessor}(); "
            + "$accessor->readAll($argv[1]); "
            + body,
            binaryDir);

        Assert.True(result.Succeeded,
            $"Reading `{scenario}` through the generated PHP failed.{Environment.NewLine}{result.Output}");
    }
}
