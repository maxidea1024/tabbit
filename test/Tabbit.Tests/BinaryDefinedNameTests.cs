using System;
using System.IO;
using System.Linq;
using Tabbit.Importers.Xlsx;
using Xunit;

namespace Tabbit.Tests;

/// <summary>
/// That a binary workbook's defined names read as its XML twin's do.
/// </summary>
/// <remarks>
/// The fixture pair is one workbook saved twice - `binary-names.xlsx` and `binary-names.xlsb`,
/// written together by `make.ps1` beside them - so every difference between the two readings
/// is the binary reader's. The shapes it holds are the ones spec/import/xlsb-defined-names.md
/// section 6 asks for: several workbook-scoped names, a sheet whose name holds a space, a
/// single cell, and one each of the shapes that are skipped - a union, a whole column, a
/// deleted target - plus a sheet-scoped name neither side may surface.
///
/// The pair is committed; regenerating it needs Excel, but reading it does not.
/// </remarks>
public class BinaryDefinedNameTests
{
    private static string FixtureDir
        => Path.Combine(RepoLayout.Root, "test", "fixtures", "xlsx", "binary-names");

    private static WorkbookPackage Xml
        => WorkbookPackage.Read(Path.Combine(FixtureDir, "binary-names.xlsx"), _ => true);

    private static WorkbookPackage Binary
        => WorkbookPackage.Read(Path.Combine(FixtureDir, "binary-names.xlsb"), _ => true);

    [Fact]
    public void Binary_names_resolve_to_the_same_rectangles_as_the_xml_ones()
    {
        var xml = Xml.DefinedNames
            .Select(n => (n.Name, n.SheetName, n.FirstRow, n.FirstColumn, n.LastRow, n.LastColumn))
            .OrderBy(n => n.Name, StringComparer.Ordinal).ToList();
        var binary = Binary.DefinedNames
            .Select(n => (n.Name, n.SheetName, n.FirstRow, n.FirstColumn, n.LastRow, n.LastColumn))
            .OrderBy(n => n.Name, StringComparer.Ordinal).ToList();

        Assert.Equal(xml, binary);
    }

    [Fact]
    public void Binary_names_are_skipped_with_the_same_reasons_as_the_xml_ones()
    {
        var xml = Xml.SkippedNames
            .Select(n => (n.Name, n.Problem))
            .OrderBy(n => n.Name, StringComparer.Ordinal).ToList();
        var binary = Binary.SkippedNames
            .Select(n => (n.Name, n.Problem))
            .OrderBy(n => n.Name, StringComparer.Ordinal).ToList();

        Assert.Equal(xml, binary);
    }

    /// <summary>
    /// The fixture's own shape, pinned - so a regeneration that quietly loses a scenario
    /// fails here rather than weakening the parity assertions above into vacuity.
    /// </summary>
    [Fact]
    public void The_fixture_holds_every_shape_the_design_names()
    {
        var package = Binary;

        var resolved = package.DefinedNames.ToDictionary(n => n.Name, StringComparer.Ordinal);
        Assert.Equal(["BasicTable", "OneCell", "SpacedSheet"], resolved.Keys.OrderBy(k => k, StringComparer.Ordinal));

        Assert.Equal("Alpha", resolved["BasicTable"].SheetName);
        Assert.Equal((0, 0, 4, 2), (resolved["BasicTable"].FirstRow, resolved["BasicTable"].FirstColumn,
                                    resolved["BasicTable"].LastRow, resolved["BasicTable"].LastColumn));

        Assert.Equal("Beta Sheet", resolved["SpacedSheet"].SheetName);
        Assert.Equal((1, 1, 8, 3), (resolved["SpacedSheet"].FirstRow, resolved["SpacedSheet"].FirstColumn,
                                    resolved["SpacedSheet"].LastRow, resolved["SpacedSheet"].LastColumn));

        Assert.Equal((2, 1, 2, 1), (resolved["OneCell"].FirstRow, resolved["OneCell"].FirstColumn,
                                    resolved["OneCell"].LastRow, resolved["OneCell"].LastColumn));

        var skipped = package.SkippedNames.ToDictionary(n => n.Name, n => n.Problem, StringComparer.Ordinal);
        Assert.Equal(WorkbookPackage.NameProblem.NotARange, skipped["Dangling"]);
        Assert.Equal(WorkbookPackage.NameProblem.NotOneRectangle, skipped["TwoParts"]);
        Assert.Equal(WorkbookPackage.NameProblem.NotOneRectangle, skipped["WholeColumn"]);

        // Sheet-scoped: a local helper, not the workbook's, and not skipped-with-a-reason
        // either - it is simply not the workbook's name to offer.
        Assert.DoesNotContain("LocalHelper", resolved.Keys);
        Assert.DoesNotContain("LocalHelper", skipped.Keys);
    }
}
