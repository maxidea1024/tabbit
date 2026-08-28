using System.Collections.Generic;
using Tabbit.Cooking;
using Tabbit.Recipe;
using Xunit;

using ValueType = Tabbit.Models.ValueType;

namespace Tabbit.Tests;

/// <summary>
/// The words a `bool` cell may be written as, and the ones a recipe adds.
/// </summary>
/// <remarks>
/// Six English words used to be written into a `switch`, which left a sheet filled in in any
/// other language choosing between writing `Y` and reading past it, keeping a second column
/// with a formula in it, or writing `1` and losing what the column means to whoever opens
/// the sheet.
///
/// Most of what follows asserts a refusal, because the declaration is where this can go
/// wrong: a list that says `예` is both true and false, or that names `0` where a rule
/// already answers it. spec/types/boolean-words.md.
/// </remarks>
public class BooleanWordTests
{
    private static CookingContext Context(RecipeModel recipe)
        => new CookingContext(new Tabbit.Models.Model(), recipe, new Diagnostics());

    private static Tabbit.Models.Location Where()
        => new Tabbit.Models.Location { Filename = "memory.xlsx", Sheet = "T", Column = 1, Row = 1 };

    /// <summary>A recipe naming the Korean words, which is what this exists for.</summary>
    private static RecipeModel Korean(bool builtin = true) => new RecipeModel
    {
        TrueWords = new List<string> { "예", "참", "켜짐", "O" },
        FalseWords = new List<string> { "아니오", "거짓", "꺼짐", "X" },
        BuiltinBoolWords = builtin,
    };

    private static bool Read(RecipeModel recipe, string cell)
        => (bool)Context(recipe).ParseValue(ValueType.Bool, null, cell, Where());

    private static string Refusal(RecipeModel recipe, string cell)
        => Assert.Throws<TabbitException>(
            () => Context(recipe).ParseValue(ValueType.Bool, null, cell, Where())).Message;

    // ------------------------------------------------------- the recipe's words

    [Theory]
    [InlineData("예", true)]
    [InlineData("참", true)]
    [InlineData("켜짐", true)]
    [InlineData("O", true)]
    [InlineData("아니오", false)]
    [InlineData("거짓", false)]
    [InlineData("꺼짐", false)]
    [InlineData("X", false)]
    public void A_declared_word_is_read(string cell, bool expected)
        => Assert.Equal(expected, Read(Korean(), cell));

    /// <summary>
    /// The built-in words are still read alongside them.
    /// </summary>
    /// <remarks>
    /// This is the whole reason adding is the default. Under a replacing default, a recipe
    /// adding `예` would lose `TRUE`, and every sheet that had been writing it would fail on
    /// the day somebody added one word for one column.
    /// </remarks>
    [Theory]
    [InlineData("TRUE", true)]
    [InlineData("yes", true)]
    [InlineData("N", false)]
    [InlineData("False", false)]
    public void The_built_in_words_are_kept(string cell, bool expected)
        => Assert.Equal(expected, Read(Korean(), cell));

    /// <summary>Case is not part of a spelling, for a declared word as for a built-in one.</summary>
    [Fact]
    public void Case_is_not_part_of_a_spelling()
    {
        Assert.True(Read(Korean(), "o"));
        Assert.False(Read(Korean(), "x"));
    }

    /// <summary>A cell is trimmed before it is looked up.</summary>
    [Fact]
    public void Surrounding_space_is_not_part_of_a_spelling()
        => Assert.True(Read(Korean(), "  예  "));

    // ------------------------------------------------------- what did not change

    /// <summary>
    /// A recipe that says nothing reads exactly what it read before.
    /// </summary>
    /// <remarks>
    /// The claim the whole design rests on, so it is asserted rather than assumed.
    /// </remarks>
    [Theory]
    [InlineData("Y", true)]
    [InlineData("YES", true)]
    [InlineData("TRUE", true)]
    [InlineData("N", false)]
    [InlineData("NO", false)]
    [InlineData("FALSE", false)]
    [InlineData("1", true)]
    [InlineData("0", false)]
    [InlineData("2", true)]
    [InlineData("", false)]
    public void A_recipe_that_names_nothing_reads_what_it_always_did(string cell, bool expected)
        => Assert.Equal(expected, Read(new RecipeModel(), cell));

    /// <summary>And a word nobody declared is still an error rather than a silent false.</summary>
    [Fact]
    public void An_unknown_word_is_still_refused()
    {
        string message = Refusal(Korean(), "Ture");

        Assert.Contains("not a boolean", message);

        // The lists are named rather than described, because they are the recipe's - a
        // sentence naming `Y/N` would be wrong for every project that added a word.
        Assert.Contains("예", message);
        Assert.Contains("아니오", message);
        Assert.Contains("TRUE", message);
    }

    // ------------------------------------------------------- turning them off

    /// <summary>
    /// `BuiltinBoolWords: false`, for a project that wants one spelling and no others.
    /// </summary>
    [Fact]
    public void The_built_in_words_can_be_turned_off()
    {
        var recipe = Korean(builtin: false);

        Assert.True(Read(recipe, "예"));
        Assert.False(Read(recipe, "아니오"));

        Assert.Contains("not a boolean", Refusal(recipe, "TRUE"));
        Assert.Contains("not a boolean", Refusal(recipe, "Y"));
    }

    /// <summary>Numbers and blanks are not words, so turning the words off leaves them.</summary>
    [Fact]
    public void Numbers_and_blanks_are_not_words()
    {
        var recipe = Korean(builtin: false);

        Assert.True(Read(recipe, "1"));
        Assert.False(Read(recipe, "0"));
        Assert.False(Read(recipe, ""));
    }

    /// <summary>
    /// Turning them off while naming nothing leaves a `bool` column no word at all.
    /// </summary>
    [Fact]
    public void Turning_them_off_with_nothing_in_their_place_is_refused()
    {
        var recipe = new RecipeModel { BuiltinBoolWords = false };

        var thrown = Assert.Throws<TabbitException>(() => Context(recipe));
        Assert.Contains("BuiltinBoolWords", thrown.Message);
    }

    /// <summary>
    /// Naming only one side is a setup rather than a mistake.
    /// </summary>
    /// <remarks>
    /// A column holding `켜짐` or nothing has said everything it needs to, because a blank
    /// cell is already false.
    /// </remarks>
    [Fact]
    public void Naming_only_one_side_is_allowed()
    {
        var recipe = new RecipeModel
        {
            TrueWords = new List<string> { "켜짐" },
            BuiltinBoolWords = false,
        };

        Assert.True(Read(recipe, "켜짐"));
        Assert.False(Read(recipe, ""));
    }

    // ------------------------------------------------------ refused declarations

    /// <summary>
    /// A word on both lists.
    /// </summary>
    /// <remarks>
    /// Reading it would need an order of precedence, and any order this picked would be one
    /// the recipe did not write down.
    /// </remarks>
    [Fact]
    public void One_word_cannot_mean_both()
    {
        var recipe = new RecipeModel
        {
            TrueWords = new List<string> { "예" },
            FalseWords = new List<string> { "예" },
        };

        Assert.Contains("both true and false", Assert.Throws<TabbitException>(() => Context(recipe)).Message);
    }

    /// <summary>The same, where the other sense is a built-in word.</summary>
    [Fact]
    public void A_built_in_word_cannot_be_declared_the_other_way()
    {
        var recipe = new RecipeModel { FalseWords = new List<string> { "TRUE" } };

        Assert.Contains("both true and false", Assert.Throws<TabbitException>(() => Context(recipe)).Message);

        // Unless the built-ins are off, which is exactly what that switch is for.
        var replaced = new RecipeModel
        {
            TrueWords = new List<string> { "예" },
            FalseWords = new List<string> { "TRUE" },
            BuiltinBoolWords = false,
        };

        Assert.False(Read(replaced, "TRUE"));
    }

    /// <summary>
    /// A word spelled as a number, which the rule about numbers already answers.
    /// </summary>
    [Theory]
    [InlineData("0")]
    [InlineData("1")]
    [InlineData("1.5")]
    public void A_number_is_not_a_word(string word)
    {
        var recipe = new RecipeModel { TrueWords = new List<string> { word } };

        Assert.Contains("is a number", Assert.Throws<TabbitException>(() => Context(recipe)).Message);
    }

    /// <summary>An entry with nothing in it. A blank cell is already false.</summary>
    [Fact]
    public void An_empty_entry_is_refused()
    {
        var recipe = new RecipeModel { TrueWords = new List<string> { "예", "  " } };

        Assert.Contains("TrueWords", Assert.Throws<TabbitException>(() => Context(recipe)).Message);
    }

    /// <summary>
    /// The mark for a cell that has no value, which is read before any word is looked for.
    /// </summary>
    [Fact]
    public void The_no_value_mark_is_not_a_word()
    {
        var recipe = new RecipeModel { FalseWords = new List<string> { "-" } };

        Assert.Contains("no value", Assert.Throws<TabbitException>(() => Context(recipe)).Message);
    }

    // ------------------------------------------------------------------ arrays

    /// <summary>Each element of a `bool[]` cell is a word of its own.</summary>
    [Fact]
    public void Array_elements_read_the_same_words()
    {
        var values = (bool[])Context(Korean())
            .ParseValue(ValueType.BoolArray, null, "예; 아니오; TRUE", Where());

        Assert.Equal(new[] { true, false, true }, values);
    }
}
