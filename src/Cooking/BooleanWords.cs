using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Tabbit.Messages;
using Tabbit.Recipe;

namespace Tabbit.Cooking;

/// <summary>
/// Which spellings a boolean cell reads as true, and which as false.
/// </summary>
/// <remarks>
/// Six English words used to be written into a `switch`, which left a sheet filled in in any
/// other language with three bad options: write `Y` and read past it every time, keep a
/// second column with a formula in it, or write `1` and lose what the column means to
/// whoever opens the sheet. A recipe adds its own words instead.
///
/// Adding is the default and replacing is a switch, so a recipe that says nothing reads
/// exactly what it read before. spec/types/boolean-words.md.
/// </remarks>
internal sealed class BooleanWords
{
    /// <summary>What a cell says when it has no value, which is not a spelling of false.</summary>
    private const string NoValueMark = "-";

    private static readonly string[] BuiltinTrue = { "Y", "YES", "TRUE" };
    private static readonly string[] BuiltinFalse = { "N", "NO", "FALSE" };

    private readonly Dictionary<string, bool> _words;

    private BooleanWords(
        Dictionary<string, bool> words, IReadOnlyList<string> trueWords,
        IReadOnlyList<string> falseWords)
    {
        _words = words;
        TrueSpellings = string.Join(", ", trueWords);
        FalseSpellings = string.Join(", ", falseWords);
    }

    /// <summary>The true spellings as one line, for the message a cell gets when it is none.</summary>
    /// <remarks>
    /// Listed rather than written into the sentence because the list is the recipe's and not
    /// this tool's - a fixed sentence naming `Y/N` would be wrong the moment anyone adds a
    /// word, and wrong in the one report that exists to say what was expected.
    /// </remarks>
    public string TrueSpellings { get; }

    /// <summary>The false spellings, likewise.</summary>
    public string FalseSpellings { get; }

    /// <summary>
    /// Reads the recipe's boolean words, reporting a declaration that cannot mean anything.
    /// </summary>
    /// <remarks>
    /// Everything here is settled before a workbook is opened, so a recipe whose two lists
    /// disagree is answered on its own terms rather than on whichever cell reached it first.
    /// </remarks>
    public static BooleanWords Of(RecipeModel recipe)
    {
        var words = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        var trueWords = new List<string>();
        var falseWords = new List<string>();

        var declaredTrue = Declared(recipe.TrueWords, nameof(recipe.TrueWords));
        var declaredFalse = Declared(recipe.FalseWords, nameof(recipe.FalseWords));

        if (recipe.BuiltinBoolWords)
        {
            foreach (string builtin in BuiltinTrue)
                Add(words, trueWords, builtin, true);

            foreach (string builtin in BuiltinFalse)
                Add(words, falseWords, builtin, false);
        }
        else if (declaredTrue.Count == 0 && declaredFalse.Count == 0)
        {
            // Turning the built-ins off and then naming nothing leaves a `bool` column that
            // reads numbers and blanks and no word at all, which no recipe means.
            //
            // Naming only one side is allowed, and is a setup rather than a mistake: a sheet
            // whose column holds `켜짐` or nothing has said everything it needs to, because a
            // blank cell is already false.
            throw new TabbitException(null, Message.Of(RecipeMessages.BoolWordsNone));
        }

        foreach (string word in declaredTrue)
            Add(words, trueWords, word, true);

        foreach (string word in declaredFalse)
            Add(words, falseWords, word, false);

        return new BooleanWords(words, trueWords, falseWords);
    }

    /// <summary>What one list declares, with each word checked on its own.</summary>
    private static List<string> Declared(List<string>? declared, string key)
    {
        var words = new List<string>();

        foreach (string entry in declared ?? new List<string>())
        {
            string word = (entry ?? "").Trim();

            // An empty entry is a list with a mistake in it rather than a way of saying
            // "a blank cell" - a blank cell is already false and needs nothing said.
            if (word.Length == 0)
                throw new TabbitException(null, Message.Of(RecipeMessages.BoolWordBlank, ("Key", key)));

            // The mark for a cell that has no value. It is read before a boolean spelling
            // is looked for, so a word spelled this way would sit in the list doing nothing.
            if (word == NoValueMark)
            {
                throw new TabbitException(null,
                    Message.Of(RecipeMessages.BoolWordNoValueMark, ("Word", word), ("Mark", NoValueMark)));
            }

            // `0` and `1.5` are already answered, by the rule that a number is true when it
            // is not zero. A word spelled as one puts two rules on the same cell.
            if (double.TryParse(
                    word, NumberStyles.Float | NumberStyles.AllowThousands,
                    CultureInfo.InvariantCulture, out _))
            {
                throw new TabbitException(null,
                    Message.Of(RecipeMessages.BoolWordIsANumber, ("Word", word)));
            }

            words.Add(word);
        }

        return words;
    }

    private static void Add(
        Dictionary<string, bool> words, List<string> spellings, string word, bool means)
    {
        if (words.TryGetValue(word, out bool already))
        {
            // The same word on both lists, whether the other one is the recipe's or built
            // in. Reading it would need an order of precedence, and any order this picked
            // would be one the recipe did not write down.
            if (already != means)
            {
                throw new TabbitException(null,
                    Message.Of(RecipeMessages.BoolWordBothSenses, ("Word", word)));
            }

            return;
        }

        words[word] = means;
        spellings.Add(word);
    }

    /// <summary>
    /// Whether a cell is one of the spellings, and which one.
    /// </summary>
    /// <remarks>
    /// Case is not part of a spelling, so `yes` and `YES` are one word - which is what the
    /// six built-in ones have always done.
    /// </remarks>
    public bool TryRead(string text, out bool value)
        => _words.TryGetValue(text.Trim(), out value);
}
