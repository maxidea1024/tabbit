using System.Collections.Generic;
using System.Linq;
using Tabbit.Cooking;
using Tabbit.Cooking.Layouts;
using Tabbit.Models;
using Tabbit.Models.Raw;
using Tabbit.Recipe;
using Xunit;

namespace Tabbit.Tests;

/// <summary>
/// A column whose sheet names the tables its value has to be a row of.
/// </summary>
/// <remarks>
/// A constraint and not a reference: the value stays the number it was, nothing is resolved,
/// and no generated code learns the column has this on it. What a sheet is saying is
/// "whatever id this holds, one of these tables has a row for it", and the whole of honouring
/// that is looking. `refs=` says the same thing in the core notation.
/// spec/references/reference-surface-naming.md section 6.
///
/// Built as grids rather than workbooks because what is under test is a layout's own rules
/// plus the check that reads them. Two tables on one sheet, which this layout allows.
///
/// spec/references/multi-target-references.md.
/// </remarks>
public class ReferencedTableTests
{
    /// <summary>
    /// A sheet holding several tables, each a block of rows with a defined name over it.
    /// </summary>
    /// <param name="blocks">
    /// One entry per table: its name, then its rows - names, types, `:`-keyed constraint
    /// rows, then data.
    /// </param>
    private static Model ParseModel(params (string Name, string[][] Rows)[] blocks)
    {
        var rows = new List<List<RawCell>>();
        var ranges = new List<RawNamedRange>();
        int width = blocks.Max(b => b.Rows.Max(r => r.Length));

        foreach (var block in blocks)
        {
            ranges.Add(new RawNamedRange
            {
                Name = block.Name,
                Row = rows.Count,
                Column = 0,
                Height = block.Rows.Length,
                Width = width,
            });

            foreach (var cells in block.Rows)
            {
                int at = rows.Count;

                rows.Add(Enumerable.Range(0, width).Select(column => new RawCell
                {
                    Location = new Location
                    {
                        Filename = "book.xlsx",
                        Sheet = "Sheet1",
                        Row = at,
                        Column = column,
                    },
                    Value = column < cells.Length ? cells[column] : "",
                }).ToList());
            }

            // A blank row between blocks, so neither range runs into the next.
            rows.Add(Enumerable.Range(0, width).Select(column => new RawCell
            {
                Location = new Location
                {
                    Filename = "book.xlsx",
                    Sheet = "Sheet1",
                    Row = rows.Count,
                    Column = column,
                },
                Value = "",
            }).ToList());
        }

        var sheet = new RawSheet
        {
            Location = new Location { Filename = "book.xlsx", Sheet = "Sheet1" },
            ColumnCount = width,
            Rows = rows,
        };

        foreach (var range in ranges)
            sheet.NamedRanges.Add(range);

        var context = new CookingContext(new Model(), new RecipeModel(), new Diagnostics());
        var parser = new UwoLayoutParser();

        parser.ParseDeclarations(context, new[] { sheet });
        parser.ParseTables(context, new[] { sheet });

        return context.Model;
    }

    /// <summary>A target table of two rows, keyed 1 and 2.</summary>
    private static (string, string[][]) Catalogue(string name, params string[] ids)
        => (name, new[]
        {
            new[] { "id", "Label" },
            new[] { "key", "number" },
        }.Concat(ids.Select(id => new[] { id, "1" })).ToArray());

    /// <summary>The rows of a holder whose `linkRow` names its targets.</summary>
    private static (string, string[][]) Holder(string linkRow, string targets, params string[] values)
        => ("Holder", new[]
        {
            new[] { "id", "TargetId" },
            new[] { "key", "number" },
            new[] { linkRow, targets },
        }.Concat(values.Select((value, at) => new[] { (at + 1).ToString(), value })).ToArray());

    private static List<(Severity Severity, TabbitException.Detail Detail)> Check(Model model)
    {
        var diagnostics = new Diagnostics();
        var holder = model.FindTable("Holder");

        new ModelCooker().ValidateReferencedTables(model, holder, holder.RowSets.First(), diagnostics);

        return diagnostics.Entries.ToList();
    }

    /// <summary>An id that one of the named tables has passes.</summary>
    /// <remarks>
    /// Two targets and the value in the second, so what is being shown is that all of them
    /// are looked in rather than only the first.
    /// </remarks>
    [Fact]
    public void An_id_in_any_of_the_named_tables_passes()
    {
        var model = ParseModel(
            Catalogue("Weapon", "10", "11"),
            Catalogue("Armour", "20", "21"),
            Holder(":links", "\"Weapon\",\n\"Armour\"", "10", "21"));

        Assert.Empty(Check(model));
    }

    /// <summary>And one that none of them has is refused, at the cell.</summary>
    [Fact]
    public void An_id_in_none_of_them_is_refused()
    {
        var model = ParseModel(
            Catalogue("Weapon", "10", "11"),
            Catalogue("Armour", "20", "21"),
            Holder(":links", "\"Weapon\",\n\"Armour\"", "10", "99"));

        var reported = Assert.Single(Check(model));

        Assert.Equal(Severity.Error, reported.Severity);
        Assert.Equal(Tabbit.Cooking.CookingMessages.MultiTargetMissingRow, reported.Detail.MessageId);
        Assert.Contains("Holder.TargetId", reported.Detail.Message);
        Assert.Contains("99", reported.Detail.Message);

        // Both tables it looked in, because "not found" is only actionable beside where
        // the search happened.
        Assert.Contains("Weapon", reported.Detail.Message);
        Assert.Contains("Armour", reported.Detail.Message);

        // The cell holding `99`, not the row that declared the constraint - taken from the
        // model rather than written out, so the assertion says which cell it means.
        Assert.Equal(
            model.FindTable("Holder").Data[1][1].RawCell.Location.Row,
            reported.Detail.Location?.Row);
    }

    /// <summary>
    /// The singular row and a list of one say the same thing.
    /// </summary>
    /// <remarks>
    /// They are separate rows in the sheets and the checker they came from runs the same
    /// lookup over both, so the model takes one target as a list of length one rather than
    /// as a different kind of declaration.
    /// </remarks>
    [Theory]
    [InlineData(":link")]
    [InlineData(":links")]
    public void One_target_reads_the_same_from_either_row(string row)
    {
        var model = ParseModel(
            Catalogue("Weapon", "10", "11"),
            Holder(row, "\"Weapon\"", "10", "99"));

        var reported = Assert.Single(Check(model));

        Assert.Equal(Tabbit.Cooking.CookingMessages.MultiTargetMissingRow, reported.Detail.MessageId);
        Assert.Contains("99", reported.Detail.Message);
        Assert.Contains("Weapon", reported.Detail.Message);
    }

    /// <summary>
    /// A target written `file/table` is the table, and the file half is dropped.
    /// </summary>
    /// <remarks>
    /// Which output file a project splits a table into is that project's business, and the
    /// core has no use for it. Naming the file alone means the two have the same name, which
    /// is what the checker this came from assumes as well.
    /// </remarks>
    [Fact]
    public void A_target_naming_a_file_and_a_table_keeps_the_table()
    {
        var model = ParseModel(
            Catalogue("Weapon", "10"),
            Holder(":links", "\"Catalogue/Weapon\"", "10"));

        Assert.Empty(Check(model));
        Assert.Equal(
            new[] { "Weapon" },
            model.FindTable("Holder").Fields[1].Constraints.ReferencedTables);
    }

    /// <summary>
    /// A build that does not contain every named table judges the column not at all.
    /// </summary>
    /// <remarks>
    /// This is the important one. A recipe reads the workbooks it names, so a table the
    /// declaration lists may not be in this build - and checking against what is left
    /// reports every id that lives in the absent one. That is how a shop's item id checked
    /// against one catalogue instead of the two it names produces thousands of findings,
    /// none of them real. So it is all of them or none.
    /// </remarks>
    [Fact]
    public void A_column_naming_a_table_this_build_lacks_is_not_checked()
    {
        var model = ParseModel(
            Catalogue("Weapon", "10"),
            // `99` is in neither, and would be a finding if the present half were checked
            // on its own.
            Holder(":links", "\"Weapon\",\n\"Armour\"", "10", "99"));

        Assert.Empty(Check(model));
    }

    /// <summary>
    /// A blank cell asks nothing.
    /// </summary>
    /// <remarks>
    /// Absence is not an id to look up, and a column that has to hold something says so by
    /// being required - a different declaration, checked elsewhere.
    /// </remarks>
    [Fact]
    public void A_cell_with_no_value_is_not_looked_up()
    {
        var model = ParseModel(
            Catalogue("Weapon", "10"),
            Holder(":links", "\"Weapon\"", "10", "-"));

        Assert.Empty(Check(model));
    }

    /// <summary>
    /// The id and the key it matches are compared as numbers, not as boxes.
    /// </summary>
    /// <remarks>
    /// The regression that matters. A key column typed `key` narrows to `int` while an
    /// ordinary `number` column stays `double`, so the two arrive boxed as different types
    /// and a set of one does not contain the other. Left that way, every row of every
    /// checked column was a finding - which is what a check reports when it is really
    /// answering "these are different types".
    /// </remarks>
    [Fact]
    public void An_id_matches_a_key_of_a_different_numeric_type()
    {
        var model = ParseModel(
            Catalogue("Weapon", "10"),
            Holder(":links", "\"Weapon\"", "10"));

        var holder = model.FindTable("Holder");
        var target = model.FindTable("Weapon");

        // The premise: the two really are different CLR types. If a later change makes them
        // the same, this test stops proving anything and should be revisited rather than
        // quietly passing.
        Assert.NotEqual(
            holder.Data[0][1].Value.GetType(),
            target.Data[0][0].Value.GetType());

        Assert.Empty(Check(model));
    }

    /// <summary>A column no row declared carries nothing.</summary>
    [Fact]
    public void A_column_with_no_such_row_is_unconstrained()
    {
        var model = ParseModel(
            Catalogue("Weapon", "10"),
            Holder(":min", "1", "10"));

        Assert.Null(model.FindTable("Holder").Fields[1].Constraints.ReferencedTables);
        Assert.Empty(Check(model));
    }

    /// <summary>
    /// Two targets holding the same id passes, because the question is not which one.
    /// </summary>
    /// <remarks>
    /// The check asks whether the value is an id of one of the named tables. Nothing narrows
    /// it to a row, so nothing has to choose between two tables that both hold it - and a
    /// project that wants ids kept apart is stating a rule of its own.
    /// spec/references/reference-surface-naming.md section 6.
    /// </remarks>
    [Fact]
    public void Targets_sharing_an_id_are_not_a_finding()
    {
        var model = ParseModel(
            Catalogue("Weapon", "10", "11"),
            Catalogue("Armour", "11", "12"),
            Holder(":links", "\"Weapon\",\n\"Armour\"", "10"));

        Assert.Empty(Check(model));
    }
}
