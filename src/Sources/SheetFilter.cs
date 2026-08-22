using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Tabbit.Recipe;
using Tabbit.Messages;

namespace Tabbit.Sources;

/// <summary>
/// Decides which workbooks of a source a recipe entry wants, and which of their sheets.
/// </summary>
/// <remarks>
/// Two lists at each level, all optional: an include list narrows to what is named, and an
/// exclude list drops from whatever is left. Neither is a layout question - a sheet that is
/// not input is not input whichever way the ones that are get read - so this sits in front
/// of every source rather than inside a parser.
///
/// The two levels are the same mechanism because they answer the same question at different
/// grain. A workbook is dropped before it is opened, which is the coarse case: a backup kept
/// for reference, or a file whose contents were never tabular. A sheet pattern may then name
/// the workbook it applies to, as `[Book]Sheet`, because sheet names repeat across workbooks
/// - `Define` in one is a table and in another is a scratch tab, and an unqualified pattern
/// cannot say which.
///
/// An include list also gets the entries it named checked off: see
/// <see cref="ReportUnmatchedIncludes"/>. A name that matched nothing is almost always a
/// typo or a renamed tab, and the failure it causes otherwise is a table missing from the
/// output with nothing in the run saying so.
/// </remarks>
public sealed class SheetFilter
{
    /// <summary>Takes everything. What a recipe entry that names no list gets.</summary>
    public static readonly SheetFilter All = new SheetFilter(
        new List<Pattern>(), new List<Pattern>(), new List<Pattern>(), new List<Pattern>());

    /// <summary>
    /// One entry of one of the four lists, compiled.
    /// </summary>
    /// <remarks>
    /// <see cref="Workbook"/> is null on a sheet pattern that names no workbook, which is
    /// the unqualified form and means every workbook. <see cref="Sheet"/> is null on a
    /// workbook pattern, which is about the workbook alone.
    /// </remarks>
    private sealed class Pattern
    {
        public string Text = "";
        public Regex? Workbook;
        public Regex? Sheet;
        public bool Matched;
    }

    private readonly List<Pattern> _workbookIncludes;
    private readonly List<Pattern> _workbookExcludes;
    private readonly List<Pattern> _includes;
    private readonly List<Pattern> _excludes;

    /// <summary>
    /// Workbooks this filter turned away, so a qualified include that named one of them can
    /// say why it matched nothing.
    /// </summary>
    private readonly List<string> _workbooksDropped = new List<string>();

    private SheetFilter(
        List<Pattern> workbookIncludes,
        List<Pattern> workbookExcludes,
        List<Pattern> includes,
        List<Pattern> excludes)
    {
        _workbookIncludes = workbookIncludes;
        _workbookExcludes = workbookExcludes;
        _includes = includes;
        _excludes = excludes;
    }

    /// <summary>Builds a filter from a recipe entry's four lists.</summary>
    /// <param name="section">Recipe path of the entry, for the message a malformed pattern gets.</param>
    public static SheetFilter From(SheetSourceRecipe recipe, string section)
    {
        if (recipe is null)
            return All;

        return new SheetFilter(
            CompileWorkbooks(recipe.IncludeWorkbooks, "IncludeWorkbooks", section),
            CompileWorkbooks(recipe.ExcludeWorkbooks, "ExcludeWorkbooks", section),
            CompileSheets(recipe.IncludeSheets, "IncludeSheets", section),
            CompileSheets(recipe.ExcludeSheets, "ExcludeSheets", section));
    }

    /// <summary>
    /// Whether a workbook should be opened at all.
    /// </summary>
    /// <param name="workbook">
    /// How the source names it: for a directory of files, the path relative to the directory
    /// searched; for a source presenting one document, its title.
    /// </param>
    public bool IncludesWorkbook(string workbook)
    {
        string name = (workbook ?? "").Trim();

        bool included = _workbookIncludes.Count == 0;

        foreach (var pattern in _workbookIncludes)
        {
            if (!MatchesWorkbook(pattern.Workbook!, name))
                continue;

            pattern.Matched = true;
            included = true;
        }

        if (included && !_workbookExcludes.Any(pattern => MatchesWorkbook(pattern.Workbook!, name)))
            return true;

        _workbooksDropped.Add(name);
        return false;
    }

    /// <summary>
    /// Whether a sheet of this name, in this workbook, should be read.
    /// </summary>
    /// <remarks>
    /// **Asked from several threads at once**, because the workbooks are read in parallel -
    /// see the import loop. The one thing this writes is `Matched`, and it only ever writes
    /// `true`: the answer does not depend on which thread got there first, and what it is for
    /// is the report at the end of the entry saying which patterns matched nothing.
    ///
    /// <see cref="IncludesWorkbook"/> is the one that appends to a list, and it is called from
    /// the sequential loop that decides what to open.
    /// </remarks>
    /// <remarks>
    /// The workbook is passed even though <see cref="IncludesWorkbook"/> has already accepted
    /// it, because a sheet pattern may name one - and a sheet name on its own is not an
    /// identity: two workbooks of one directory can both have a `Define` tab.
    /// </remarks>
    public bool Includes(string workbook, string sheetName)
    {
        string book = (workbook ?? "").Trim();
        string name = (sheetName ?? "").Trim();

        // Recorded even when the sheet is excluded further down, because the question
        // this answers is "did the recipe name something that is not there" - and a
        // sheet named by both lists is there.
        bool included = _includes.Count == 0;

        foreach (var pattern in _includes)
        {
            if (!Matches(pattern, book, name))
                continue;

            pattern.Matched = true;
            included = true;
        }

        if (!included)
            return false;

        return !_excludes.Any(pattern => Matches(pattern, book, name));
    }

    /// <summary>
    /// Throws naming the entries of the two include lists that nothing ever matched. Call
    /// once the source has offered every workbook and sheet it has.
    /// </summary>
    /// <param name="section">Recipe path of the entry, for the message.</param>
    /// <param name="workbooks">Every workbook the source found, to suggest from.</param>
    /// <param name="sheets">Every sheet it saw, with the workbook each was in.</param>
    public void ReportUnmatchedIncludes(
        string section,
        IEnumerable<string> workbooks,
        IEnumerable<(string Workbook, string Sheet)> sheets)
    {
        // Workbooks first: naming one that is not there is the coarser mistake, and an
        // unmatched sheet pattern is what it causes when the two are written together.
        var missingWorkbooks = _workbookIncludes
            .Where(pattern => !pattern.Matched)
            .Select(pattern => pattern.Text)
            .ToList();

        if (missingWorkbooks.Count > 0)
        {
            throw new TabbitException(null,
                Message.Of(Importers.ImportMessages.WorkbooksNotFound,
                    ("Section", section), ("Count", missingWorkbooks.Count),
                    ("Missing", string.Join(", ", missingWorkbooks)),
                    ("Present", Listed(workbooks))));
        }

        var missing = _includes.Where(pattern => !pattern.Matched).ToList();
        if (missing.Count == 0)
            return;

        // Qualified names on both sides of the message when any missing pattern named a
        // workbook, because that is the case where the plain name is not the answer: the
        // sheet may well exist, in a workbook the pattern did not name.
        bool qualified = missing.Any(pattern => pattern.Workbook is not null);

        var names = (sheets ?? Enumerable.Empty<(string Workbook, string Sheet)>())
            .Select(seen => qualified ? $"[{seen.Workbook}]{seen.Sheet}" : seen.Sheet);

        // Two ids rather than one message with an optional line appended. The extra line is
        // what usually explains the report - a pattern matched nothing because the workbook
        // holding its sheet was never opened - and a list of the sheets that were read cannot
        // say that.
        bool skipped = _workbooksDropped.Count > 0;

        throw new TabbitException(null, skipped
            ? Message.Of(Importers.ImportMessages.SheetsNotFoundWithSkipped,
                ("Section", section), ("Count", missing.Count),
                ("Missing", string.Join(", ", missing.Select(pattern => pattern.Text))),
                ("Present", Listed(names)),
                ("Skipped", Listed(_workbooksDropped)))
            : Message.Of(Importers.ImportMessages.SheetsNotFound,
                ("Section", section), ("Count", missing.Count),
                ("Missing", string.Join(", ", missing.Select(pattern => pattern.Text))),
                ("Present", Listed(names))));
    }

    /// <summary>Sorted, de-duplicated and readable, or `(none)` when there is nothing.</summary>
    private static string Listed(IEnumerable<string> names)
    {
        var listed = (names ?? Enumerable.Empty<string>())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return listed.Count > 0 ? string.Join(", ", listed) : "(none)";
    }

    /// <summary>Whether a sheet pattern covers this sheet of this workbook.</summary>
    private static bool Matches(Pattern pattern, string workbook, string sheetName)
    {
        if (pattern.Workbook is not null && !MatchesWorkbook(pattern.Workbook, workbook))
            return false;

        return pattern.Sheet!.IsMatch(sheetName);
    }

    /// <summary>
    /// Whether a workbook glob covers a workbook.
    /// </summary>
    /// <remarks>
    /// Three spellings of the same workbook are accepted, because all three are what somebody
    /// writing the recipe would reach for: the relative path as the source names it, the file
    /// name alone, and the file name without its extension. So `Items`, `Items.xlsx` and
    /// `shared/Items.xlsx` all name one workbook, while `backup/*` names a directory of them
    /// and `*.xlsb` names a format.
    /// </remarks>
    private static bool MatchesWorkbook(Regex glob, string workbook)
    {
        // `\` on the way in because .NET's own directory walk produces it and a recipe is
        // written with `/`.
        string path = (workbook ?? "").Trim().Replace('\\', '/');

        if (glob.IsMatch(path))
            return true;

        int slash = path.LastIndexOf('/');
        string name = slash >= 0 ? path.Substring(slash + 1) : path;

        if (slash >= 0 && glob.IsMatch(name))
            return true;

        int dot = name.LastIndexOf('.');

        return dot > 0 && glob.IsMatch(name.Substring(0, dot));
    }

    private static List<Pattern> CompileWorkbooks(IEnumerable<string> patterns, string key, string section)
    {
        var result = new List<Pattern>();

        foreach (string text in Cleaned(patterns))
        {
            if (text.StartsWith("[", StringComparison.Ordinal))
            {
                throw new TabbitException(null,
                    Message.Of(Recipe.RecipeMessages.WorkbookPatternHasSheet,
                        ("Section", section), ("Text", text), ("Key", key)));
            }

            result.Add(new Pattern { Text = text, Workbook = ToRegex(text) });
        }

        return result;
    }

    /// <summary>
    /// Compiles a sheet list, reading the optional `[workbook]` a pattern may open with.
    /// </summary>
    /// <remarks>
    /// Brackets rather than a separator character, because a sheet name may contain almost
    /// anything - `!` and `.` included - while Excel forbids `[` and `]` in one. So the
    /// qualifier cannot be mistaken for part of a name, and a pattern that opens a bracket
    /// and never closes it is answered rather than read as a sheet whose name starts with a
    /// bracket.
    /// </remarks>
    private static List<Pattern> CompileSheets(IEnumerable<string> patterns, string key, string section)
    {
        var result = new List<Pattern>();

        foreach (string text in Cleaned(patterns))
        {
            if (!text.StartsWith("[", StringComparison.Ordinal))
            {
                result.Add(new Pattern { Text = text, Sheet = ToRegex(text) });
                continue;
            }

            int close = text.IndexOf(']');
            if (close < 0)
            {
                throw new TabbitException(null,
                    Message.Of(Recipe.RecipeMessages.SheetPatternUnclosedBracket,
                        ("Section", section), ("Text", text), ("Key", key)));
            }

            string workbook = text.Substring(1, close - 1).Trim();
            string sheet = text.Substring(close + 1).Trim();

            if (workbook.Length == 0)
            {
                throw new TabbitException(null,
                    Message.Of(Recipe.RecipeMessages.SheetPatternNoWorkbook,
                        ("Section", section), ("Text", text), ("Key", key)));
            }

            if (sheet.Length == 0)
            {
                throw new TabbitException(null,
                    Message.Of(Recipe.RecipeMessages.SheetPatternNoSheet,
                        ("Section", section), ("Text", text), ("Key", key),
                        ("Alternative", key.StartsWith("Include", StringComparison.Ordinal)
                            ? "Include" : "Exclude")));
            }

            result.Add(new Pattern
            {
                Text = text,
                Workbook = ToRegex(workbook),
                Sheet = ToRegex(sheet),
            });
        }

        return result;
    }

    /// <summary>The entries that say something, trimmed.</summary>
    private static IEnumerable<string> Cleaned(IEnumerable<string> patterns)
    {
        if (patterns is null)
            yield break;

        foreach (var raw in patterns)
        {
            string text = (raw ?? "").Trim();
            if (text.Length == 0)
                continue;

            yield return text;
        }
    }

    /// <summary>
    /// Turns a glob into a whole-string regex, with every other character taken literally.
    /// </summary>
    private static Regex ToRegex(string glob)
    {
        var expression = new StringBuilder("^");

        foreach (char c in glob)
        {
            switch (c)
            {
                case '*': expression.Append(".*"); break;
                case '?': expression.Append('.'); break;
                default: expression.Append(Regex.Escape(c.ToString())); break;
            }
        }

        expression.Append('$');

        // Case-insensitive because a sheet tab is typed by hand in two places - the tab
        // and the recipe - and a project that renames `ItemTable` to `Itemtable` has not
        // changed which sheet it means. The same for a workbook: Windows does not
        // distinguish the two spellings of its name either.
        return new Regex(expression.ToString(), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }
}
