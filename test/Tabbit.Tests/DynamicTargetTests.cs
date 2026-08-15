using System;
using Xunit;

namespace Tabbit.Tests;

/// <summary>
/// The recipe's `Targets` list: output entries that name their target by id rather
/// than sitting in a section of their own.
///
/// It exists so that adding a target - a new output language, say - does not mean
/// extending RecipeModel with another section and another list. The named sections
/// stay for the recipes already using them, which makes equivalence between the two
/// forms the thing worth testing.
/// </summary>
public class DynamicTargetTests
{
    /// <summary>
    /// The strongest statement available: the same seven outputs written through
    /// `Targets` match, byte for byte, the tree the named sections produce.
    ///
    /// Both recipes carry identical settings and differ only in the output root, and
    /// nothing a target writes embeds its own path - the `core` in the golden files is
    /// the namespace, not the directory.
    /// </summary>
    [Fact]
    public void Targets_list_produces_the_same_tree_as_the_named_sections()
    {
        var result = TabbitRunner.Convert("core-dynamic");

        Assert.True(result.Succeeded,
            $"Conversion failed.{Environment.NewLine}{result.Describe()}");

        GoldenComparer.Verify("core-dynamic", goldenScenario: "core");
    }

    /// <summary>
    /// A `Type` no target answers to is a mistake, not an entry to skip: a recipe that
    /// asks for output and gets none without being told has shipped a build missing a
    /// file somebody expected.
    /// </summary>
    [Fact]
    public void Unknown_target_type_is_rejected_and_the_known_ids_are_listed()
    {
        var result = TabbitRunner.Convert("targets-unknown-type");

        Assert.False(result.Succeeded, "A `Targets` entry naming a nonexistent target was accepted.");

        Assert.Contains("Targets[0]", result.StdOut);
        Assert.Contains("pyton", result.StdOut);

        // The message has to say what is available, or the reader is left guessing at
        // the spelling of the one they wanted.
        Assert.Contains("binary", result.StdOut);
        Assert.Contains("typescript", result.StdOut);
    }

    [Fact]
    public void Target_entry_without_a_type_is_rejected()
    {
        var result = TabbitRunner.Convert("targets-missing-type");

        Assert.False(result.Succeeded, "A `Targets` entry with no `Type` was accepted.");
        Assert.Contains("Targets[0]", result.StdOut);
        Assert.Contains("Type", result.StdOut);
    }

    /// <summary>
    /// A misspelled setting has to fail rather than fall back to the default.
    ///
    /// `FileExtention` for `FileExtension` would otherwise write `.tcb` files for a
    /// recipe that asked for `.bytes`, and the only symptom would be the setting
    /// appearing to do nothing.
    /// </summary>
    [Fact]
    public void Unknown_setting_on_a_target_entry_is_rejected()
    {
        var result = TabbitRunner.Convert("targets-unknown-setting");

        Assert.False(result.Succeeded, "A misspelled setting on a `Targets` entry was accepted.");
        Assert.Contains("Targets[0]", result.StdOut);
        Assert.Contains("FileExtention", result.StdOut);
    }
}
