using System.Collections.Generic;
using System.IO;
using Tabbit.Cooking;
using Tabbit.Models;
using Xunit;

namespace Tabbit.Tests;

/// <summary>
/// Colour palettes: the built-in one, and the ones a recipe reads from a file.
/// </summary>
/// <remarks>
/// A palette is **data**, so this is where the file format and the failure modes are pinned.
/// The rule the whole design rests on is that a bare name is always the built-in `css` palette:
/// adding a palette must not be able to change what `red` means in a workbook that already
/// converts, and the two ways that could happen - a bare name resolving elsewhere, and a
/// recipe replacing `css` - are both refused here.
///
/// spec/types/composite-value-types.md section 4.4.
/// </remarks>
public class ColorPaletteTests : System.IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "tabbit-palettes-" + Path.GetRandomFileName());

    public ColorPaletteTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); }
        catch (IOException) { /* a temp directory left behind is not a test failure. */ }
    }

    private string WriteFile(string name, string content)
    {
        string path = Path.Combine(_dir, name);
        File.WriteAllText(path, content);
        return path;
    }

    private static CookingContext Context(params (string Name, string Path)[] palettes)
    {
        var recipe = new Tabbit.Recipe.RecipeModel();

        foreach (var (name, path) in palettes)
            recipe.Palettes[name] = path;

        return new CookingContext(new Model(), recipe, new Diagnostics());
    }

    private static Location Where()
        => new Location { Filename = "memory.xlsx", Sheet = "T", Column = 1, Row = 1 };

    private static int[] Color32(CookingContext context, string cell)
        => (int[])context.ParseValue(ValueType.Color32, null, cell, Where())!;

    // ------------------------------------------------------------------ reading

    [Fact]
    public void A_declared_palette_is_reached_by_naming_it()
    {
        string path = WriteFile("brand.json", """
            {
              "primary":   "#3366CC",
              "blue.500":  "#2196F3",
              "faded":     "#FFFFFF80"
            }
            """);

        var context = Context(("brand", path));

        Assert.Equal(new[] { 51, 102, 204, 255 }, Color32(context, "brand.primary"));

        // A dotted colour name is the palette's business, not this tool's - only the first dot
        // separates the palette from what it holds.
        Assert.Equal(new[] { 33, 150, 243, 255 }, Color32(context, "brand.blue.500"));

        // Eight digits, so the palette carries an alpha.
        Assert.Equal(new[] { 255, 255, 255, 128 }, Color32(context, "brand.faded"));
    }

    /// <summary>
    /// A bare name is the built-in palette, whatever else the recipe declared.
    /// </summary>
    /// <remarks>
    /// This is the whole point of the qualification rule. The palette below defines `red` as
    /// something else, and a sheet writing `red` must still get the CSS one - otherwise adding
    /// a palette silently changes colours in workbooks that have nothing to do with it.
    /// </remarks>
    [Fact]
    public void A_bare_name_is_always_the_built_in_palette()
    {
        string path = WriteFile("brand.json", """{ "red": "#00FF00" }""");

        var context = Context(("brand", path));

        Assert.Equal(new[] { 255, 0, 0, 255 }, Color32(context, "red"));
        Assert.Equal(new[] { 0, 255, 0, 255 }, Color32(context, "brand.red"));
    }

    [Fact]
    public void A_palette_is_read_case_insensitively()
    {
        string path = WriteFile("brand.json", """{ "Primary": "#3366CC" }""");

        var context = Context(("Brand", path));

        Assert.Equal(new[] { 51, 102, 204, 255 }, Color32(context, "brand.PRIMARY"));
    }

    // ----------------------------------------------------------------- refusals

    /// <summary>
    /// The built-in palette cannot be replaced.
    /// </summary>
    [Fact]
    public void A_recipe_may_not_declare_a_palette_called_css()
    {
        string path = WriteFile("mine.json", """{ "red": "#00FF00" }""");

        var thrown = Assert.Throws<TabbitException>(() => Context(("css", path)));

        Assert.Contains("built-in", thrown.Message);
        Assert.Contains("red", thrown.Message);
    }

    /// <summary>
    /// A palette file is read when the recipe is, not when a coloured cell happens to be.
    /// </summary>
    /// <remarks>
    /// A missing file is a fault in the recipe. Reporting it against the first cell that
    /// mentions a colour would send the author to a sheet that is not wrong.
    /// </remarks>
    [Fact]
    public void A_missing_palette_file_is_refused_before_any_cell_is_read()
    {
        var thrown = Assert.Throws<TabbitException>(
            () => Context(("brand", Path.Combine(_dir, "absent.json"))));

        Assert.Contains("brand", thrown.Message);
        Assert.Contains("does not exist", thrown.Message);
    }

    [Fact]
    public void A_palette_that_is_not_json_names_itself()
    {
        string path = WriteFile("broken.json", "{ not json");

        var thrown = Assert.Throws<TabbitException>(() => Context(("brand", path)));

        Assert.Contains("brand", thrown.Message);
        Assert.Contains("JSON", thrown.Message);
    }

    /// <summary>
    /// An entry that is not a colour is refused with the entry's name.
    /// </summary>
    /// <remarks>
    /// A palette is authored by hand and a wrong entry is silent everywhere else: the colour
    /// would simply be missing, and the report would name the cell that asked for it rather
    /// than the file that failed to define it.
    /// </remarks>
    [Theory]
    [InlineData("""{ "primary": "3366CC" }""")]
    [InlineData("""{ "primary": "#GGGGGG" }""")]
    [InlineData("""{ "primary": "#33" }""")]
    [InlineData("""{ "primary": [1, 2, 3] }""")]
    public void A_palette_entry_that_is_not_a_colour_is_refused(string content)
    {
        string path = WriteFile("broken.json", content);

        var thrown = Assert.Throws<TabbitException>(() => Context(("brand", path)));

        Assert.Contains("primary", thrown.Message);
    }

    /// <summary>
    /// And the two ways a name fails to resolve stay separate reports.
    /// </summary>
    [Fact]
    public void An_undeclared_palette_and_a_missing_entry_read_differently()
    {
        string path = WriteFile("brand.json", """{ "primary": "#3366CC" }""");

        var context = Context(("brand", path));

        Assert.Contains("No palette called `other`", Refusal(context, "other.primary"));
        Assert.Contains("has no colour called `secondary`", Refusal(context, "brand.secondary"));

        // And the report lists what this run does know, so the fix is in the message.
        Assert.Contains("`brand`", Refusal(context, "other.primary"));
        Assert.Contains("`css`", Refusal(context, "other.primary"));
    }

    private static string Refusal(CookingContext context, string cell)
        => Assert.Throws<TabbitException>(
            () => context.ParseValue(ValueType.Color32, null, cell, Where())).Message;
}
