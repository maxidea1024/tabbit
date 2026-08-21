using Tabbit.Recipe;
using Tabbit.Models;
using System.IO;
using Serilog;
using Tabbit.Helpers;
using System.Collections.Generic;
using System.Linq;
using Tabbit.Extensions;
using System;
using System.Globalization;
using Tabbit.Targets;

namespace Tabbit.Exporters;

/// <summary>
/// One .json file per table.
///
/// This is what the generated TypeScript reads.
/// </summary>
public class JsonRecipe : IOutputRecipe
{
    /// <summary>Output directory. Created if it does not exist.</summary>
    public string Path { get; set; } = "";

    /// <summary>
    /// Writes each row as a bare array of values instead of an object with
    /// field names.
    ///
    /// Smaller, at the cost of being unreadable on its own. The generated
    /// readers handle both, deciding from the shape of the first row.
    /// </summary>
    public bool UseCompactRowFormat { get; set; } = false;

    /// <summary>
    /// Pretty-prints the output. Worth it while inspecting data by hand, not
    /// for something a program will read.
    /// </summary>
    public bool Indented { get; set; } = false;

    /// <summary>
    /// Which side this output is built for: "c", "s", or "cs"/blank for
    /// both. Entities and fields marked for the other side are left out.
    ///
    /// Declare the same side on the exporter and on the code generator
    /// that reads its files: the two must agree on the column set or the
    /// generated reader will not match the data.
    /// </summary>
    public string TargetSide { get; set; } = "cs";

    /// <summary>Removes files this run did not write.</summary>
    /// <remarks>
    /// On, because the output is a file per table: rename or delete a table and
    /// its old file stays behind. A stale data file is worse than a stale source
    /// file - it ships, it costs transfer, and a build still asking for the old
    /// name reads it, which is old values from a rollback nobody performed.
    ///
    /// Only files the manifest already lists are removed. That ledger is this
    /// tool's own record of what it put here, so a directory holding anything
    /// else is untouchable - the file has to have been written by a previous run
    /// to be removable by this one.
    /// </remarks>
    public bool Sweep { get; set; } = true;
}

[TabbitTarget("json", TargetKind.Export, Order = 20)]
public class JsonExporter : Target<JsonRecipe>
{
    /// <summary>Which step of a run this class's log lines belong to.</summary>
    private static Serilog.ILogger Log => LogCategory.Exporting;

    private Manifest _manifest = null!;

    /// <summary>
    /// JSON carries a record directly, so there is nothing to refuse.
    /// </summary>
    /// <remarks>
    /// The named row format nests: a record becomes an object and an array of records an
    /// array of them. The compact format is positional over the table's columns and a
    /// record's members are ordinary columns, so it stays flat - which is what compact
    /// means, and what a reader indexing by column position needs.
    /// </remarks>
    protected override bool SupportsNestedFields => true;

    /// <summary>
    /// A level is an object keyed by its members' names or an array when they have none, and
    /// that answer does not change with depth - see <see cref="Compose"/>.
    /// </summary>
    protected override bool SupportsDeepNestedFields => true;

    protected override bool SupportsOptionalFields => true;

    /// <summary>
    /// An absent element is `null` in an array, which is the whole of what this format needs
    /// to say it. The binary and the readers follow in their own step -
    /// spec/nullable-array-elements.md.
    /// </summary>
    protected override bool SupportsOptionalElements => true;

    protected override void Run(TargetContext context, JsonRecipe recipe)
    {
        // An entry left in the recipe with a blank path is treated as switched off.
        if (string.IsNullOrEmpty(recipe.Path))
            return;

        string manifestFilename = Path.Combine(recipe.Path, "manifest-json.json");

        _manifest = Manifest.Load(manifestFilename);

        // Before anything is written, while the ledger is still the previous run's: a
        // table renamed or removed leaves its file behind otherwise.
        if (recipe.Sweep)
            _manifest.PruneStaleFiles(recipe.Path);

        // context.Model is already narrowed to this entry's target side.
        foreach (var table in context.Model.Tables)
            ExportTable(recipe, table);

        _manifest.BuildAndWriteToFile(manifestFilename);
    }

    /// <summary>
    /// One cell as JSON, which is `null` when the sheet gave an optional column no value.
    /// </summary>
    /// <remarks>
    /// The distinction the whole feature is for, and JSON has a word for it. Without this
    /// the two read paths would disagree - the binary says absent, the JSON says zero -
    /// which is exactly what the TypeScript round trip exists to catch.
    /// </remarks>
    private static object? ForJson(List<Cell> row, Field? field)
    {
        var cell = row[field!.Index];

        // A record's member is never absent on its own, so it is never null here either.
        // A record is one thing to its consumer; what a record array does express is how
        // many elements a row filled in, and that is the array's length. The member is
        // still marked `?` in sheets that trim, because that is how a cell says it holds no
        // value - it just does not reach the output as an absence.
        if (!field.IsRequired && !cell.HasValue && !field.IsRecordMember)
            return null;

        // An array whose elements may be absent: the array is there and some of its places
        // are not, which JSON writes the same way it writes an absent value.
        // spec/nullable-array-elements.md.
        if (cell.ElementHasValue is { } present && cell.Value is Array elements)
        {
            var items = new object?[elements.Length];

            for (int at = 0; at < elements.Length; at++)
            {
                items[at] = at < present.Length && !present[at]
                    ? null
                    : ForJson(elements.GetValue(at));
            }

            return items;
        }

        return ForJson(cell.Value);
    }

    /// <summary>
    /// Adjusts a value for JSON, where the format cannot carry it faithfully - or would
    /// carry it in a spelling JSON does not have.
    ///
    /// **64-bit integers** go out as strings. JSON has one numeric type and every reader
    /// treats it as a double, so a value past 2^53 is silently rounded on the way in -
    /// JavaScript turns 9007199254740993 into ...992 without complaint. Written as a
    /// string, it survives, and a reader that wants the number reconstructs it
    /// exactly. This is the same choice Protocol Buffers makes for int64 in its JSON
    /// mapping, and for the same reason.
    ///
    /// **Whole doubles and floats** lose the trailing `.0`. That suffix is .NET telling
    /// itself which CLR type the number came from; JSON has no such distinction, so `1.0`
    /// and `1` are one value and the shorter spelling is the one everything else writes.
    /// Keeping it made a `double` column disagree with hand-written JSON on every integral
    /// row, which is a difference that means nothing and hides the ones that do.
    /// </summary>
    private static object? ForJson(object? value)
    {
        switch (value)
        {
            case long i64:
                return i64.ToString(CultureInfo.InvariantCulture);

            // Only inside long's range, where the conversion is exact - a double past it is
            // integral too, and rounding it to a long would change the number.
            case double f64 when IsWholeAndExactAsInt64(f64):
                return (long)f64;

            case float f32 when IsWholeAndExactAsInt64(f32):
                return (long)f32;

            case Array array:
            {
                var items = new object[array.Length];
                for (int i = 0; i < array.Length; i++)
                    items[i] = ForJson(array.GetValue(i))!;

                return items;
            }

            default:
                return value;
        }
    }

    /// <summary>
    /// Whether a floating-point value is a whole number that a 64-bit integer holds exactly.
    /// </summary>
    /// <remarks>
    /// The range test is not about precision but about the cast: `(long)1e30` is undefined
    /// behaviour's managed cousin, an unspecified value. Inside the range the conversion is
    /// exact both ways - an integral double is an integer, and reading that integer back
    /// gives the same double - so the shorter spelling loses nothing.
    ///
    /// NaN and the infinities fail every test here and fall through to whatever the
    /// serializer already did with them.
    /// </remarks>
    private static bool IsWholeAndExactAsInt64(double value)
        => value == Math.Floor(value)
           && value >= long.MinValue
           && value <= long.MaxValue;

    /// <summary>
    /// One element of a record group: position <paramref name="element"/> of every member,
    /// keyed by the member's name - or indexed, when the members have no names.
    /// </summary>
    private static object RecordElement(SerialField group, List<Cell> row, int element)
        => Compose(group.Members, row, element);

    /// <summary>
    /// One level of a record: an object keyed by its members' names, or an array when they
    /// have none.
    /// </summary>
    /// <remarks>
    /// This and <see cref="MemberValue"/> are one recursion, and between them they write
    /// every shape the notation can say. What used to be three methods - an element of an
    /// array of records, a record whose members are arrays, an array of arrays - were three
    /// readings of two questions asked at one level: does this level repeat, and does it
    /// have names. Asking them per level is what makes the depth stop mattering.
    ///
    /// See spec/nested-multi-level.md.
    /// </remarks>
    private static object Compose(List<RecordMember> members, List<Cell> row, int element)
    {
        // Nothing to key an object by, so the level is an array - which is what the notation
        // said when it numbered the level instead of naming it.
        if (members.Count > 0 && members.TrueForAll(member => member.IsAnonymous))
        {
            var items = new object?[members.Count];
            for (int at = 0; at < items.Length; at++)
                items[at] = MemberValue(members[at], row, element);

            return items;
        }

        var result = new Dictionary<string, object?>();

        foreach (var member in members)
            result.Add(member.Name.ToCamelCase(), MemberValue(member, row, element));

        return result;
    }

    /// <summary>
    /// What one member holds: a value, all of its elements when the level repeats, or the
    /// level below it.
    /// </summary>
    private static object? MemberValue(RecordMember member, List<Cell> row, int element)
    {
        // The level repeats, so it holds all of its elements rather than one of them. The
        // element number this member was asked for belongs to a level above it, and there is
        // only ever one level carrying it - the folding requires that.
        if (member.IsArray)
        {
            int count = member.IsLeaf ? member.Fields.Count : member.Leaves.First().Fields.Count;
            var values = new object?[count];

            for (int e = 0; e < count; e++)
                values[e] = member.IsLeaf ? ForJson(row, member.Fields[e]!) : Compose(member.Members, row, e);

            return values;
        }

        return member.IsLeaf
            ? ForJson(row, member.Fields[element]!)
            : Compose(member.Members, row, element);
    }

    /// <summary>
    /// One file per set of rows the table has.
    /// </summary>
    /// <remarks>
    /// A table with one set - which is nearly all of them - yields one file, so this is the
    /// ordinary path rather than a branch around it. spec/table-row-sets.md.
    /// </remarks>
    private void ExportTable(JsonRecipe recipe, Table table)
    {
        foreach (var rowSet in table.RowSets)
            ExportRowSet(recipe, table, rowSet);
    }

    private void ExportRowSet(JsonRecipe recipe, Table table, RowSet rowSet)
    {
        string fileName = table.DataFileName + rowSet.Name;
        var rows = rowSet.Rows;

        var filename = Path.Combine(recipe.Path, fileName + ".json");
        filename = Path.GetFullPath(filename);

        Log.Information($"Exporting json file `{filename}`");

        object? sourceRows = null;

        if (recipe.UseCompactRowFormat)
        {
            var writableRows = new List<object?[]>();

            // Projected through the table's wire columns rather than over the raw row or
            // the field list. A row always carries every column the sheet declared, while
            // this output is meant to hold what the table has - they differ as soon as a
            // field is filtered out by target side.
            //
            // Wire-column order, which is what "compact" has always claimed to be: the
            // generated readers walk a compact row with a running offset, taking N entries
            // per group, and that only lines up if a group's entries are adjacent. Sheet
            // order does not guarantee it - a group's columns are allowed to sit apart -
            // and for a record group it is never true, because the members interleave.
            //
            // No existing output moves: every group in every committed fixture happens to
            // have its columns adjacent and in order, which is why the two orders agreed
            // for as long as nothing tested otherwise.
            // One entry per cell, except that a column whose length is this row's contributes
            // a single nested entry - exactly as the binary gives it one variable-length
            // block instead of a fixed run. A positional format has no other way to say it:
            // a reader walking with a running offset cannot advance past a count it was never
            // told, so the count has to be the array's own length.
            foreach (var row in rows)
            {
                var rawData = new List<object?>();

                foreach (var wire in table.WireColumns)
                {
                    if (wire.IsVariableLengthArray && !wire.Group.IsVariableLengthArray)
                    {
                        int elements = table.ElementCountIn(wire.Group, row);
                        var values = new object[elements];

                        for (int e = 0; e < elements; e++)
                            values[e] = ForJson(row, wire.Cells[e])!;

                        rawData.Add(values);
                        continue;
                    }

                    foreach (var cell in wire.Cells)
                        rawData.Add(ForJson(row, cell!));
                }

                writableRows.Add(rawData.ToArray());
            }

            sourceRows = writableRows;
        }
        else
        {
            var writableRows = new List<Dictionary<string, object?>>();
            foreach (var row in rows)
            {
                var dataRow = new Dictionary<string, object?>();

                foreach (var sf in table.SerialFields)
                {
                    string name = sf.Name.ToCamelCase();

                    // Indexed through each field's own column, not a running
                    // counter over the groups.
                    //
                    // A serial field collapses N columns into one named entry, so
                    // there are fewer groups than columns. Walking a counter
                    // therefore took the first column of each group and then
                    // drifted: every value after the first array landed under the
                    // wrong name, and the remaining columns were dropped entirely.
                    if (sf.IsRecord)
                    {
                        // A record's members each hold one column per element, so the
                        // object for element k is built by reading position k of every
                        // member. The folding has already required them to line up.
                        if (sf.MembersAreArrays)
                        {
                            // The same columns as the case below, turned inside out: each
                            // member holds all of its elements. Keyed by name, or indexed
                            // when the level was numbered rather than named - which is the
                            // difference between a record of arrays and an array of arrays,
                            // and the only difference between them.
                            dataRow.Add(name, RecordElement(sf, row, 0));
                        }
                        else if (sf.IsArray)
                        {
                            // The row's count, which is the declared one unless the table
                            // trims the elements its author left empty.
                            var elements = new object[table.ElementCountIn(sf, row)];
                            for (int e = 0; e < elements.Length; e++)
                                elements[e] = RecordElement(sf, row, e);

                            dataRow.Add(name, elements);
                        }
                        else
                        {
                            dataRow.Add(name, RecordElement(sf, row, 0));
                        }
                    }
                    else if (sf.IsVariableLengthArray)
                    {
                        // The cell already parsed into an array; gathering it
                        // across the group's fields would nest it one deep.
                        dataRow.Add(name, ForJson(row, sf.FirstField!));
                    }
                    else if (sf.IsArray)
                    {
                        // The row's count, which is the declared one unless the table trims
                        // the columns its author left empty at the end.
                        int elements = table.ElementCountIn(sf, row);

                        // Every element answers for itself. A folded array has no cell that
                        // stands for the array, so there is nothing here that could say the
                        // array as a whole is absent - and the reading that did say it took
                        // element 0's cell for the array's, losing elements 1..N with it.
                        // spec/nullable-array-elements.md.
                        dataRow.Add(name, sf.Fields.Take(elements)
                            .Select(f => ForJson(row, f))
                            .ToArray());
                    }
                    else
                    {
                        dataRow.Add(name, ForJson(row, sf.FirstField!));
                    }
                }

                writableRows.Add(dataRow);
            }

            sourceRows = writableRows;
        }

        string stagingFilename = StagingFiles.WriteToJsonFile(filename, sourceRows, recipe.Indented);
        _manifest.Add(fileName + ".json", stagingFilename);
    }
}
