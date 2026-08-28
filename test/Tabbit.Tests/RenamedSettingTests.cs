using System;
using System.IO;
using Tabbit.Recipe;
using Xunit;

namespace Tabbit.Tests;

/// <summary>
/// A recipe still holding a setting's old name.
/// </summary>
/// <remarks>
/// Newtonsoft ignores a key nothing binds to, so without this a recipe left holding
/// `ArrayDelimiter` would convert with `DefaultDelimiter`'s default - a project whose sheets
/// write `1|2|3` reading every array cell as one value, on a run that says nothing about it.
///
/// That is the failure this whole tool is built against, so the old name is read and
/// reported rather than ignored. spec/types/value-delimiter.md section 4.
/// </remarks>
public class RenamedSettingTests : IDisposable
{
    private readonly string _dir;

    public RenamedSettingTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "tabbit-renamed-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* a temp directory */ }
    }

    private RecipeModel Load(string json)
    {
        string path = Path.Combine(_dir, "recipe.json");
        File.WriteAllText(path, json);

        return RecipeModel.LoadFromFile(path)!;
    }

    /// <summary>The old name still names the setting, so no existing recipe stops working.</summary>
    [Fact]
    public void The_old_name_is_read()
        => Assert.Equal("|", Load("{ \"ArrayDelimiter\": \"|\" }").DefaultDelimiter);

    /// <summary>The new one, which is what a recipe should be holding.</summary>
    [Fact]
    public void The_new_name_is_read()
        => Assert.Equal("|", Load("{ \"DefaultDelimiter\": \"|\" }").DefaultDelimiter);

    /// <summary>
    /// A source entry writes the same setting, and is renamed with the recipe.
    /// </summary>
    /// <remarks>
    /// The rewrite walks the whole document rather than its top level, because the two
    /// places are one rename to whoever has to do it.
    /// </remarks>
    [Fact]
    public void A_source_entry_is_renamed_too()
    {
        var recipe = Load(@"{
            ""Sources"": { ""Xlsx"": [ { ""Path"": ""sheets"", ""ArrayDelimiter"": ""|"" } ] }
        }");

        Assert.Equal("|", recipe.Sources.Xlsx[0].DefaultDelimiter);
    }

    /// <summary>
    /// Both names on one object.
    /// </summary>
    /// <remarks>
    /// There is no reading of that which is not a guess about which one the author meant to
    /// keep, and the two may hold different values.
    /// </remarks>
    [Fact]
    public void Both_names_at_once_is_refused()
    {
        var thrown = Assert.Throws<TabbitException>(
            () => Load("{ \"ArrayDelimiter\": \"|\", \"DefaultDelimiter\": \",\" }"));

        Assert.Contains("ArrayDelimiter", thrown.Message);
        Assert.Contains("DefaultDelimiter", thrown.Message);
    }

    /// <summary>A recipe that names neither keeps the default.</summary>
    [Fact]
    public void A_recipe_naming_neither_keeps_the_default()
        => Assert.Equal(";", Load("{ }").DefaultDelimiter);
}
