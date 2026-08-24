using System;
using Xunit;

namespace Tabbit.Tests;

/// <summary>
/// End-to-end regression tests: convert a fixture workbook and compare every
/// produced artifact against a committed golden tree.
///
/// These exist so the .NET 10 port and the feature work that follows it have a
/// way to tell "I changed the output" from "I broke the output". A port must
/// leave every golden file untouched; a deliberate fix updates specific goldens
/// and the resulting diff is the review artifact.
/// </summary>
public class ConversionGoldenTests
{
    [Theory]
    // Everything Tabbit handles: all primitive types, enums (both an explicit
    // zero entry and an auto-inserted one), constants, cross-table record
    // references, serial fields and per-field target sides.
    [InlineData("core")]
    // Values entered as real Excel types rather than text - notably genuine date
    // cells, which are numbers carrying a date format.
    [InlineData("excel-typed")]
    // Leading blank rows and columns, a ragged interior blank row, and two
    // entities on one sheet.
    [InlineData("layout-edge")]
    // The `RefTable.RefFieldName` foreign form, which resolves to the referenced
    // field's type while storing the target's index.
    [InlineData("foreign-field")]
    // The same workbook built for one side only, so entities and fields marked
    // for the other side must be absent from every artifact.
    [InlineData("core-client")]
    [InlineData("core-server")]
    // The `Group.Member` notation: a record, an array of records whose members are of
    // different types, and a scalar serial field beside them that must still fold the way
    // it always has. JSON only for now - the code targets refuse a record by name until
    // each one learns the shape, which NestedTargetSupportTests covers.
    [InlineData("nested")]
    // The trailing `?`: every type that refuses a blank cell, paired with the same type
    // marked optional, and rows where all of the marked ones are blank at once.
    [InlineData("optional")]
    // What a cell says about having nothing: a blank read as the type's own empty value, `-`
    // read as no value at all, and `\-` as the character itself. JSON holds the three side by
    // side and the binary tree says which of them the presence bit follows.
    // spec/blank-and-null-cells.md.
    [InlineData("blank-and-null")]
    // The four spellings of an array's optionality, in one table: `int[]`, `int[]?`, `int?[]`
    // and `int?[]?`, with a `string?[]` beside them where an empty element and an absent one
    // look the same to a value comparison. JSON only - the format cannot say it yet.
    // spec/nullable-array-elements.md.
    [InlineData("nullable-elements")]
    // A record array whose length is each row's: trailing empty elements dropped, a gap in
    // the middle kept, and an authored zero left alone.
    [InlineData("record-trim")]
    [InlineData("member-array")]
    // A record whose member is itself a record, with a value beside that record at the same
    // level. C# and binary, because what is worth pinning is the pair: a struct per level in
    // the generated page, and the fixed-array column per leaf in the file. The wire is what
    // says the depth cost the format nothing. spec/nested-multi-level.md.
    [InlineData("nested-deep")]
    // A primary index that is a string, beside an `int` secondary one - so the golden shows
    // the generated lookup keyed by the field's own type in both languages.
    [InlineData("string-index")]
    // Every type a key may be - `bigint`, `uuid`, `enum` - each keying one table and each
    // also a secondary index beside a different primary. Every language, because
    // nothing here is new below the generators and everything is new inside them: each picks
    // the key's spelling from its own type table and builds its own dictionary. Recording it
    // found two languages that did not: PHP subscripted an array with an object, and C++
    // named a key type `std::hash` had no specialization for.
    [InlineData("key-types")]
    // Keys made of several columns, and the multi-argument lookups they generate. Every
    // language, for the same reason `key-types` is: the wire does not move - a key's columns
    // are ordinary columns - and every generated reader grows a surface it did not have.
    // The three tables ask three things: an int-and-enum pair, two strings holding
    // `("a b", "c")` beside `("a", "b c")` so the key's encoding is pinned rather than
    // assumed, and a key of three columns. Recording it found PHP casting an enum object to
    // an int. spec/primary-layout.md section 3.5.
    [InlineData("composite-key")]
    // The other half of that: a table pointing *at* those keys. One table holds a `string`, a
    // `bigint` and a `uuid` reference at once, so a mixture is pinned as well as each on its
    // own. What travels is the key, and `int32` used to be written in for it - in the
    // exporters, in the format's element mapping, in the read switches and in the templates'
    // member declarations - so the whole tree is the record that every one of those now asks
    // the target instead. spec/reference-key-types.md.
    [InlineData("reference-keys")]
    // Columns typed `text`: gathered files beside the ordinary exports. Both halves are in
    // the tree on purpose - the gathered files are the new output, and the JSON, the binary
    // and the generated C# beside them are what says a role changed none of it. A role that
    // ever reaches an exported byte shows up here as a diff in a file that has nothing to do
    // with the feature.
    [InlineData("text")]
    // Composite columns - `vec3f`, `vec2i`, `quat`, `color32`, `color` - each row writing its
    // values in a different notation. What this tree pins is that they arrive as records: the
    // JSON nests, the file holds one column per component, and three languages declare the
    // struct their own way. `CompositeExpansionTests` holds the other half, which is that the
    // same table written with `Pos.X` columns produces the same bytes.
    // spec/composite-value-types.md.
    [InlineData("composite")]
    // A `bitset` column beside a `bigint` one holding the same values. What this tree pins is
    // an absence: the type exists for as long as parsing lasts, and the cooker folds it to a
    // 64-bit integer once every cell has been read - so no artifact here may be able to tell
    // the two columns apart. Three languages, because each picks its own spelling for the
    // width and `long`, `bigint` and `int64_t` are three of them. spec/bitset.md.
    [InlineData("bitset")]
    // A table given its rows twice. What the tree has to show is one type for `Colour` and
    // none for `Colour_alt`, and one data file each - spec/table-row-sets.md.
    [InlineData("row-sets")]
    // A reference that is a member of a record group, in every shape a record group has: an
    // array of records, one record, one record of arrays, a reference two levels in, a target
    // keyed by a string, and a trimmed array whose length is the row's. All thirteen
    // languages, because the format did not change and the generators did - a record is
    // stored one column per member, and a reference member is a column carrying its target's
    // key like any other, so a diff in the binary here would be a defect rather than a
    // feature. `Loadout` carries two references to one table in each element, which is what
    // decided that the key lives inside the element. spec/references-in-records.md.
    [InlineData("record-ref")]
    // An array of references: numbered reference columns folded into one array, in both forms
    // a reference takes - a whole row and one of that row's values. Every language,
    // because this is the shape `foreign[]`'s refusal points at and no fixture held one: every
    // generator emitted code for it that nothing ever read, and two of them wrote the sheet's
    // column count into the linking pass. spec/nullable-array-elements.md.
    [InlineData("serial-ref")]
    // A column whose value is a row of one of several tables, written `Weapon|Armour` - the
    // notation the core layout grew so this shape could be declared without a project's own
    // constraint row. Every language, because what each adds is a slot, a discriminator and a
    // narrowing accessor per target, all of them spelled per language. Both file formats too,
    // and neither may move: the column already travelled as the target's key, so a diff in the
    // binary or the JSON here is a defect rather than a feature.
    // spec/multi-target-accessors.md.
    [InlineData("multi-target")]
    public void Fixture_matches_golden(string scenario)
    {
        var result = TabbitRunner.Convert(scenario);

        Assert.True(result.Succeeded,
            $"Conversion of `{scenario}` failed.{Environment.NewLine}{result.Describe()}");

        GoldenComparer.Verify(scenario);
    }
}
