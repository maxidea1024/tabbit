using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Serilog;
using Tabbit.CodeGeneration;
using Tabbit.Helpers;
using Tabbit.Models;
using Tabbit.Targets;

namespace Tabbit.Exporters;

/// <summary>
/// Settings for the gathered-text target.
///
/// Declared beside its exporter and reached through the recipe's `Targets` list.
/// </summary>
public sealed class TextRecipe : IOutputRecipe
{
    /// <summary>Output directory. Created if it does not exist.</summary>
    public string Path { get; set; } = "";

    /// <summary>
    /// Extension of each group's file.
    /// </summary>
    /// <remarks>
    /// It also decides how `{text}` is escaped - see <see cref="TextExporter.QuotingFor"/>.
    /// A `.json` file and an `.xml` file need different escaping and the extension already
    /// says which is being written, so nothing else has to.
    /// </remarks>
    public string FileExtension { get; set; } = ".txt";

    /// <summary>
    /// One gathered string, as a line of the output file.
    /// </summary>
    /// <remarks>
    /// <code>
    ///     "Format": "NSLOCTEXT(\"{namespace}\", \"{group}\", \"{text}\")"
    /// </code>
    ///
    /// The names in braces are filled in per entry; <see cref="TextExporter"/> lists them.
    /// `{{` and `}}` are a literal brace, which gathered strings really do contain - `{0}`
    /// is an ordinary thing for a sentence with a number in it to hold.
    ///
    /// **This target ships no default format.** What reads a gathered file is somebody's
    /// engine or their translation pipeline, and there is no shape this tool could pick that
    /// would be right for the next project - so it asks rather than guessing and being wrong
    /// quietly. A format that a line pattern cannot express is what <see cref="Template"/> is
    /// for; name one of the two.
    /// </remarks>
    public string Format { get; set; } = "";

    /// <summary>
    /// A line written before the entries. Blank writes none.
    /// </summary>
    /// <remarks>
    /// Takes the placeholders that do not belong to one entry - `{group}`, `{namespace}` and
    /// `{count}` - because a heading is about the file rather than about a string in it.
    /// </remarks>
    public string Header { get; set; } = "";

    /// <summary>A line written after the entries, in the same form as <see cref="Header"/>.</summary>
    public string Footer { get; set; } = "";

    /// <summary>
    /// Written at the end of every entry but the last. Blank writes none.
    /// </summary>
    /// <remarks>
    /// A setting rather than something the pattern could hold, because the pattern cannot see
    /// which entry is last - and a comma after the last one is exactly what makes a JSON
    /// document invalid. With this, a header and a footer, a bracketed format can be written
    /// here instead of reaching for <see cref="Template"/>.
    ///
    /// Not derived from the extension the way the escaping is: `.json` is a document needing
    /// commas or a stream of lines needing none, and only the recipe knows which it is writing.
    /// </remarks>
    public string Separator { get; set; } = "";

    /// <summary>
    /// What `{namespace}` fills in with, for the columns that did not declare one.
    /// </summary>
    /// <remarks>
    /// A column says its group's own with the second part of the group - `text(Achievement,
    /// Quests)` - and that wins. This is the answer for every group no column spoke for, which
    /// in most projects is all of them: one namespace for the export, or none at all.
    ///
    /// A pipeline wanting one namespace per file needs neither - that is `{group}`.
    /// </remarks>
    public string Namespace { get; set; } = "";

    /// <summary>
    /// A Scriban template, for a shape a line pattern cannot say.
    /// </summary>
    /// <remarks>
    /// A bare name is one of the templates this tool ships - `textset-unreal` is the only one,
    /// and it is the worked example of the line pattern above written out in full. Anything
    /// holding a directory separator or ending in `.sbn` is read from disk, relative to the
    /// working directory.
    ///
    /// For the formats with something to decide per entry: a line that differs when the string
    /// came from one table rather than another, a document with real structure around the
    /// list. Naming this and <see cref="Format"/> both is refused - two answers to what a file
    /// looks like is not a fallback order, it is a mistake.
    /// </remarks>
    public string Template { get; set; } = "";

    /// <summary>
    /// Line ending to write: `lf` or `crlf`.
    /// </summary>
    /// <remarks>
    /// LF by default, as every other file this tool writes. It is a setting at all because a
    /// gathered file is usually joining a tree somebody else's tool wrote, and a whole-file
    /// diff on every run is how a real difference gets missed.
    /// </remarks>
    public string LineEnding { get; set; } = "lf";

    /// <summary>Removes files this run did not write.</summary>
    /// <remarks>
    /// On, because the output is a file per group: rename a table, or move a column into
    /// another group, and the old file stays behind holding strings nothing declares any more.
    /// A stale text set is worse than a stale source file - a translator works from it.
    ///
    /// Only files the manifest already lists are removed, which is this tool's own record of
    /// what it put here. The marker every generated source file carries is no use for this
    /// target: what reads these files is not a compiler, and there is no comment syntax this
    /// tool can assume.
    /// </remarks>
    public bool Sweep { get; set; } = true;

    /// <summary>Which side this output is built for: "c", "s", or "cs"/blank for both.</summary>
    public string TargetSide { get; set; } = "cs";
}

/// <summary>
/// Gathers the strings of every column marked as text into one file per group.
/// </summary>
/// <remarks>
/// What separates a gathered string from any other is only that somebody downstream needs the
/// list of them - to translate, to check a font covers them, to key a string table. The value
/// itself still goes out through the ordinary exports, unchanged and in the same place, so a
/// build that ignores this target reads exactly what it read before.
///
/// The grouping is per column and defaults to the table's name, which is the arrangement a set
/// of sheets already has: one table's strings belong together because a table is a subject.
/// A column that says otherwise - `text(Common)` on a dozen tables - collects across them.
///
/// **The format is the recipe's.** <see cref="TextRecipe.Format"/> is a line pattern and
/// covers the shapes that are one line per string, which is most of them;
/// <see cref="TextRecipe.Template"/> is a Scriban template for the rest. What this file holds
/// is the gathering and the placeholders, and no opinion about what a line looks like.
///
/// The placeholders a pattern may use:
///
/// <code>
///     {text}       the string, escaped for the extension being written
///     {raw}        the string exactly as the sheet holds it
///     {group}      the group, which is also the file's name
///     {namespace}  the namespace the group belongs to: what a column declared with
///                  `text(Achievement,Quests)`, or the entry's Namespace setting
///     {table}      the table the string was found in
///     {field}      the column it was found in
///     {location}   workbook, sheet and cell of the first row holding it
///     {index}      position in the file, from 1
///     {count}      how many entries the file holds        (header and footer only)
/// </code>
/// </remarks>
[TabbitTarget("text", TargetKind.Export, Order = 25)]
public sealed class TextExporter : Target<TextRecipe>
{
    /// <summary>
    /// How a value is made safe to sit inside the file being written.
    /// </summary>
    public enum Quoting
    {
        /// <summary>`\` and `"` take a backslash. What most quoted formats want.</summary>
        Backslash,

        /// <summary>`&amp;` `&lt;` `&gt;` `"` `'` become entities.</summary>
        Xml,

        /// <summary>`"` is doubled, which is how a quoted csv field carries one.</summary>
        Csv,

        /// <summary>Tab and the line breaks are escaped, since in this format they separate.</summary>
        Tsv,

        /// <summary>A JSON string's contents, control characters included.</summary>
        Json,
    }

    /// <summary>
    /// One entry: a string that was gathered, and where it came from.
    /// </summary>
    /// <remarks>
    /// A class rather than a tuple because a Scriban template reads it by member name, and
    /// those names are the contract a project's own template is written against.
    /// </remarks>
    public sealed class Entry
    {
        /// <summary>The string exactly as the sheet holds it.</summary>
        /// <remarks>
        /// The only form stored. <see cref="Text"/> is computed when something asks, because a
        /// real export gathers hundreds of thousands of strings and a format uses one escaping.
        /// </remarks>
        public required string Raw { get; set; }

        /// <summary>
        /// The string made safe to sit inside the file being written.
        /// </summary>
        /// <remarks>
        /// Which escaping that is comes from the output's extension - see
        /// <see cref="TextExporter.QuotingFor"/> - rather than from a setting, because a format
        /// has one right answer and the recipe has already said which format it is writing.
        ///
        /// This one is called `text` and the untouched value is `raw`, rather than the other
        /// way round, because nearly every format quotes the string: `"{text}"`. Handing back
        /// the unescaped value under that name would produce a file that parses until the
        /// first row containing a quotation mark, and then produce one that does not - which
        /// is the failure a default should not have.
        /// </remarks>
        public string Text => Quote(Raw, Quoting);

        /// <summary>How this file quotes a value. Taken from the entry's extension.</summary>
        public Quoting Quoting { get; set; }

        /// <summary>Table the string was found in.</summary>
        public required string Table { get; set; }

        /// <summary>Column the string was found in.</summary>
        public required string Field { get; set; }

        /// <summary>Workbook, sheet and cell of the first row holding it.</summary>
        public required string Location { get; set; }
    }

    /// <summary>One output file: a group and everything gathered into it.</summary>
    public sealed class Group
    {
        /// <summary>Name of the group, which is also the file's name.</summary>
        public required string Name { get; set; }

        /// <summary>
        /// The namespace this group belongs to.
        /// </summary>
        /// <remarks>
        /// On the group and not on each string, because a namespace is the wider of the two: a
        /// group sits inside one, the way a file sits inside a folder. Per string it would
        /// invert that - one file's entries scattered across namespaces - and no pipeline
        /// downstream reads them that way.
        ///
        /// Which means the columns gathering into one group have to agree, and
        /// <see cref="Gather"/> refuses it when they do not. A column that declares none is not
        /// a disagreement; it takes whatever the group already has.
        /// </remarks>
        public required string Namespace { get; set; }

        /// <summary>What was gathered, in the order it was found.</summary>
        public List<Entry> Entries { get; set; } = new List<Entry>();

        /// <summary>The column that first named the namespace, for the message when two clash.</summary>
        internal Field? NamespaceDeclaredBy { get; set; }
    }

    /// <summary>What a Scriban template reads.</summary>
    public sealed class View
    {
        /// <summary>The group this file is for, its namespace included.</summary>
        public required Group Group { get; set; }
    }

    /// <summary>
    /// A record's member columns are ordinary columns to this target, and a role sits on the
    /// column. Nothing here reads a group's shape, so there is nothing for it to get wrong.
    /// </summary>
    protected override bool SupportsNestedFields => true;

    /// <inheritdoc cref="SupportsNestedFields"/>
    protected override bool SupportsDeepNestedFields => true;

    /// <summary>
    /// A column with no value in a row contributes nothing, which is what a blank cell means
    /// here whether or not the column is marked optional.
    /// </summary>
    protected override bool SupportsOptionalFields => true;

    protected override void Run(TargetContext context, TextRecipe recipe)
    {
        // An entry left in the recipe with a blank path is treated as switched off.
        if (string.IsNullOrEmpty(recipe.Path))
            return;

        string newline = ResolveNewline(recipe);

        bool hasFormat = !string.IsNullOrEmpty(recipe.Format);
        bool hasTemplate = !string.IsNullOrWhiteSpace(recipe.Template);

        if (hasFormat && hasTemplate)
        {
            throw new TabbitException(
                "The `text` target was given both a `Format` and a `Template`. They are two "
                + "answers to what a file looks like; name one.");
        }

        if (!hasFormat && !hasTemplate)
        {
            throw new TabbitException(
                "The `text` target needs a `Format` - one gathered string as a line of the "
                + "file. For example:\n"
                + "    \"Format\": \"NSLOCTEXT(\\\"{namespace}\\\", \\\"{group}\\\", \\\"{text}\\\")\"\n"
                + "  The names in braces are filled in per string: {text} {raw} {group} "
                + "{namespace} {table} {field} {location} {index}.\n"
                + "  A shape that is not one line per string is what `Template` is for.");
        }

        var groups = Gather(
            context.Model, recipe.Namespace ?? "", QuotingFor(recipe.FileExtension));

        string manifestFilename = System.IO.Path.Combine(recipe.Path, "manifest-text.json");
        var manifest = Manifest.Load(manifestFilename);

        // Before anything is written, while the ledger is still the previous run's: a group
        // that no column names any more leaves its file behind otherwise.
        if (recipe.Sweep)
            manifest.PruneStaleFiles(recipe.Path);

        // Parsed once for the whole run rather than per line: a real export writes hundreds of
        // thousands of them, and a pattern is the same pattern every time.
        var entryLine = hasFormat ? LinePattern.Parse(recipe.Format, "Format", forEntry: true) : null;
        var headerLine = LinePattern.Parse(recipe.Header, "Header", forEntry: false);
        var footerLine = LinePattern.Parse(recipe.Footer, "Footer", forEntry: false);

        string? templateSource = hasTemplate ? LoadTemplate(recipe.Template) : null;
        string? templateName = hasTemplate ? TemplateNameOf(recipe.Template) : null;

        foreach (var group in groups)
        {
            string name = group.Name + recipe.FileExtension;
            string filename = System.IO.Path.GetFullPath(
                System.IO.Path.Combine(recipe.Path, name));

            Log.Information(
                $"Exporting text file `{filename}` ({group.Entries.Count} string(s))");

            string rendered = hasFormat
                ? Write(group, recipe, entryLine!, headerLine, footerLine)
                : TemplateEngine.RenderSource(
                    templateName!,
                    templateSource!,
                    new View { Group = group });

            if (newline != "\n")
                rendered = rendered.Replace("\n", newline);

            manifest.Add(name, StagingFiles.WriteAllTextToFile(filename, rendered));
        }

        manifest.BuildAndWriteToFile(manifestFilename);
    }

    /// <summary>
    /// One file, from the line patterns.
    /// </summary>
    private static string Write(
        Group group, TextRecipe recipe,
        LinePattern entry, LinePattern? header, LinePattern? footer)
    {
        var result = new StringBuilder();

        string separator = recipe.Separator ?? "";

        if (header is not null)
        {
            header.Render(result, null, group, 0);
            result.Append('\n');
        }

        for (int at = 0; at < group.Entries.Count; at++)
        {
            entry.Render(result, group.Entries[at], group, at + 1);

            // After every entry but the last, which is the whole reason it is a setting and
            // not something the pattern holds.
            if (separator.Length > 0 && at < group.Entries.Count - 1)
                result.Append(separator);

            result.Append('\n');
        }

        if (footer is not null)
        {
            footer.Render(result, null, group, 0);
            result.Append('\n');
        }

        return result.ToString();
    }

    /// <summary>
    /// Every gathered string of the model, by group, each in the order it was first met.
    /// </summary>
    /// <remarks>
    /// Deduplicated within a group, because the list exists to be worked through by hand and
    /// the same sentence twice is the same work twice. Across groups it is not: two files are
    /// two sets, and a string in both is in both.
    ///
    /// Order is table order, then row order, then column order - the order somebody scrolling
    /// the sheets would meet them in. It has to be decided by something stable rather than by
    /// a hash set's enumeration, since the output is committed and diffed.
    /// </remarks>
    private static List<Group> Gather(Model model, string fallbackNamespace, Quoting quoting)
    {
        var groups = new List<Group>();
        var byName = new Dictionary<string, Group>(StringComparer.Ordinal);
        var seen = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        foreach (var table in model.Tables)
        {
            // Which columns are gathered is a property of the table, so it is answered once
            // rather than per row - the difference is 270,000 iterations on a real workbook.
            var gathered = new List<Field>();

            foreach (var field in table.Fields)
            {
                if (field.Role == StringRole.Text)
                    gathered.Add(field);
            }

            if (gathered.Count == 0)
                continue;

            foreach (var row in table.Data)
            {
                foreach (var field in gathered)
                {
                    // A column of the sheet this row is too short for. Rows are built to the
                    // field list, so this does not happen - and a target that indexes into a
                    // row should say so rather than throw from the indexer.
                    if (field.Index >= row.Count)
                        continue;

                    var cell = row[field.Index];

                    if (!cell.HasValue)
                        continue;

                    foreach (string text in Strings(cell.Value!))
                    {
                        // Whitespace is not a string somebody translates, and a column of
                        // mostly-empty cells is ordinary in a sheet.
                        if (string.IsNullOrWhiteSpace(text))
                            continue;

                        string groupName = field.RoleGroup ?? table.Name;

                        if (!byName.TryGetValue(groupName, out var group))
                        {
                            group = new Group
                            {
                                Name = groupName,

                                // The recipe's answer until a column gives one. A group whose
                                // columns all stay quiet keeps it.
                                Namespace = fallbackNamespace,
                            };

                            byName.Add(groupName, group);
                            seen.Add(groupName, new HashSet<string>(StringComparer.Ordinal));
                            groups.Add(group);
                        }

                        ClaimNamespace(group, field, table);

                        if (!seen[groupName].Add(text))
                            continue;

                        group.Entries.Add(new Entry
                        {
                            Raw = text,
                            Quoting = quoting,
                            Table = table.Name,
                            Field = field.Name,
                            Location = cell.RawCell?.Location?.ToString() ?? "",
                        });
                    }
                }
            }
        }

        // By name, so the run order of the tables cannot reorder the files - and so a
        // directory listing reads the same as the manifest.
        groups.Sort((left, right) => string.CompareOrdinal(left.Name, right.Name));

        return groups;
    }

    /// <summary>
    /// Records the namespace a column declared for its group, or refuses a second answer.
    /// </summary>
    /// <remarks>
    /// A group is one file inside one namespace, so the columns feeding it have to agree. The
    /// alternative is a file whose strings belong to two namespaces, which is not a thing the
    /// pipelines that read these files can express - so the disagreement would be resolved by
    /// whichever column happened to be read first.
    ///
    /// A column that declares nothing agrees with anything. That is the ordinary case: one
    /// column names the namespace and the rest of the group inherits it, exactly as they
    /// inherit the recipe's when none of them names one.
    /// </remarks>
    private static void ClaimNamespace(Group group, Field field, Table table)
    {
        if (field.RoleNamespace is null)
            return;

        if (group.NamespaceDeclaredBy is null)
        {
            group.Namespace = field.RoleNamespace;
            group.NamespaceDeclaredBy = field;
            return;
        }

        if (string.Equals(group.Namespace, field.RoleNamespace, StringComparison.Ordinal))
            return;

        var first = group.NamespaceDeclaredBy;

        throw new TabbitException(field.TypeLocation,
            $"Group `{group.Name}` is gathered into two namespaces.\n"
            + $"  `{table.Name}.{field.Name}` says `{field.RoleNamespace}`.\n"
            + $"  `{first.OwnerTable?.Name}.{first.Name}` says `{group.Namespace}`. "
            + $"({first.TypeLocation})\n"
            + $"  A group is one file and a namespace holds files, so the columns gathering "
            + $"into one have to agree. Split the group, or name the namespace on one column "
            + $"and let the rest take it.");
    }

    /// <summary>
    /// The strings one cell holds: one for a `text` column, and each element for a list of
    /// them.
    /// </summary>
    private static IEnumerable<string> Strings(object value)
    {
        if (value is string single)
        {
            yield return single;
            yield break;
        }

        // A delimited cell, already split when the value was parsed. Every element is gathered
        // for the same reason the single value is - the role is what the column holds, and a
        // list holds more of the same.
        if (value is string[] many)
        {
            foreach (string element in many)
                yield return element;
        }
    }


    // ------------------------------------------------------------------ escaping

    /// <summary>
    /// The escaping a file of this extension needs.
    /// </summary>
    /// <remarks>
    /// Derived rather than configured. A format has one right answer - `\"` inside an XML
    /// attribute is a backslash and then the end of the attribute, and a literal newline
    /// inside a JSON string is a parse error - and the recipe has already said which format it
    /// is writing by naming the extension. Asking it a second time is asking it to contradict
    /// itself later.
    ///
    /// An extension not listed takes the backslash form, which is what the quoted formats this
    /// tool has no name for use. `.textset` is one of them.
    /// </remarks>
    public static Quoting QuotingFor(string extension)
    {
        switch ((extension ?? "").Trim().ToLowerInvariant())
        {
            case ".json":
            case ".jsonl":
            case ".ndjson": return Quoting.Json;

            case ".xml":
            case ".xlf":
            case ".xliff":
            case ".resx":
            case ".htm":
            case ".html": return Quoting.Xml;

            case ".csv": return Quoting.Csv;
            case ".tsv": return Quoting.Tsv;

            default: return Quoting.Backslash;
        }
    }

    /// <summary>
    /// Escapes a value for the file it is going into.
    /// </summary>
    /// <remarks>
    /// Only what would end the value early or break the document. A tab or a newline survives
    /// wherever the format can carry one, because a gathered string is prose: an author who put
    /// a line break in a sentence meant it, and a translator should be shown what they wrote.
    /// </remarks>
    public static string Quote(string text, Quoting quoting)
    {
        if (string.IsNullOrEmpty(text))
            return "";

        switch (quoting)
        {
            case Quoting.Csv: return text.IndexOf('"') < 0 ? text : text.Replace("\"", "\"\"");
            case Quoting.Xml: return XmlEscape(text);
            case Quoting.Json: return JsonEscape(text);
            case Quoting.Tsv: return TsvEscape(text);
            default: return BackslashEscape(text);
        }
    }

    private static string BackslashEscape(string text)
    {
        if (text.IndexOf('\\') < 0 && text.IndexOf('"') < 0)
            return text;

        var result = new StringBuilder(text.Length + 8);

        foreach (char character in text)
        {
            if (character == '\\' || character == '"')
                result.Append('\\');

            result.Append(character);
        }

        return result.ToString();
    }

    /// <summary>
    /// Escapes for XML text and attributes both, which is the same set.
    /// </summary>
    /// <remarks>
    /// The ampersand is handled first, or the ones belonging to the entities written after it
    /// get escaped again. Both quote characters, because whether the value lands in an
    /// attribute is the format's business and not this method's.
    /// </remarks>
    private static string XmlEscape(string text)
    {
        if (text.IndexOfAny(new[] { '&', '<', '>', '"', '\'' }) < 0)
            return text;

        var result = new StringBuilder(text.Length + 16);

        foreach (char character in text)
        {
            switch (character)
            {
                case '&': result.Append("&amp;"); break;
                case '<': result.Append("&lt;"); break;
                case '>': result.Append("&gt;"); break;
                case '"': result.Append("&quot;"); break;
                case '\'': result.Append("&apos;"); break;
                default: result.Append(character); break;
            }
        }

        return result.ToString();
    }

    /// <summary>
    /// Escapes the characters that separate in a tab-separated file.
    /// </summary>
    /// <remarks>
    /// The one format here with no quoting at all: a tab inside a value is another column and
    /// a newline is another row, so both have to go. The backslash goes with them, since it is
    /// what the other two now begin.
    /// </remarks>
    private static string TsvEscape(string text)
    {
        if (text.IndexOfAny(new[] { '\\', '\t', '\r', '\n' }) < 0)
            return text;

        var result = new StringBuilder(text.Length + 8);

        foreach (char character in text)
        {
            switch (character)
            {
                case '\\': result.Append("\\\\"); break;
                case '\t': result.Append("\\t"); break;
                case '\r': result.Append("\\r"); break;
                case '\n': result.Append("\\n"); break;
                default: result.Append(character); break;
            }
        }

        return result.ToString();
    }

    /// <summary>
    /// Escapes the contents of a JSON string, control characters included.
    /// </summary>
    /// <remarks>
    /// The one escaping here that does touch tabs and newlines, and it has to: JSON has no
    /// literal line break inside a string, so leaving one produces a file no reader accepts.
    /// Everything below 0x20 with no short form goes out as `\uXXXX`.
    /// </remarks>
    private static string JsonEscape(string text)
    {
        var result = new StringBuilder(text.Length + 16);

        foreach (char character in text)
        {
            switch (character)
            {
                case '\\': result.Append("\\\\"); break;
                case '"': result.Append("\\\""); break;
                case '\n': result.Append("\\n"); break;
                case '\r': result.Append("\\r"); break;
                case '\t': result.Append("\\t"); break;
                case '\b': result.Append("\\b"); break;
                case '\f': result.Append("\\f"); break;

                default:
                    if (character < ' ')
                    {
                        result.Append("\\u").Append(
                            ((int)character).ToString("x4", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        result.Append(character);
                    }
                    break;
            }
        }

        return result.ToString();
    }


    // ------------------------------------------------------------------ the line pattern

    /// <summary>What one `{name}` in a pattern stands for.</summary>
    private enum Slot
    {
        Literal,
        Text,
        Raw,
        Group,
        Namespace,
        Table,
        Field,
        Location,
        Index,
        Count,
    }

    /// <summary>
    /// A pattern parsed into the runs of literal text and the placeholders between them.
    /// </summary>
    /// <remarks>
    /// A parse rather than a series of string replacements, for a reason that is not
    /// performance: replacement is applied to a result that already holds the values, so a
    /// gathered string containing `{group}` - or `{0}`, which real sentences are full of -
    /// would be substituted into on the next pass. A pattern scanned once cannot reach the
    /// values it filled in.
    /// </remarks>
    private sealed class LinePattern
    {
        private readonly List<(Slot Slot, string? Literal)> _parts = new List<(Slot, string?)>();

        /// <summary>
        /// Reads a pattern, or null when the recipe left it blank.
        /// </summary>
        /// <param name="setting">Which recipe field this is, so a message can name it.</param>
        /// <param name="forEntry">
        /// Whether the per-entry placeholders are in scope. A header is written once for the
        /// file and has no string to describe, so `{text}` there is a mistake rather than a
        /// blank - and saying so beats writing the header with a hole in it.
        /// </param>
        public static LinePattern? Parse(string pattern, string setting, bool forEntry)
        {
            if (string.IsNullOrEmpty(pattern))
                return null;

            var result = new LinePattern();
            var literal = new StringBuilder();

            for (int at = 0; at < pattern.Length; at++)
            {
                char character = pattern[at];

                // `{{` and `}}` are one brace. Gathered strings hold braces - `{0} 애호가` is
                // an ordinary sentence - so a pattern needs a way to write one that is not a
                // placeholder.
                if ((character == '{' || character == '}')
                    && at + 1 < pattern.Length && pattern[at + 1] == character)
                {
                    literal.Append(character);
                    at++;
                    continue;
                }

                if (character != '{')
                {
                    literal.Append(character);
                    continue;
                }

                int close = pattern.IndexOf('}', at + 1);
                if (close < 0)
                {
                    throw new TabbitException(
                        $"The `text` target's `{setting}` opens a `{{` at position {at} and never "
                        + $"closes it: `{pattern}`. Write `{{{{` for a literal brace.");
                }

                if (literal.Length > 0)
                {
                    result._parts.Add((Slot.Literal, literal.ToString()));
                    literal.Clear();
                }

                result._parts.Add((
                    SlotOf(pattern.Substring(at + 1, close - at - 1).Trim(),
                        setting, forEntry, pattern),
                    null));

                at = close;
            }

            if (literal.Length > 0)
                result._parts.Add((Slot.Literal, literal.ToString()));

            return result;
        }

        private static Slot SlotOf(string name, string setting, bool forEntry, string pattern)
        {
            switch (name.ToLowerInvariant())
            {
                case "text": return forEntry ? Slot.Text : Refuse();
                case "raw": return forEntry ? Slot.Raw : Refuse();
                case "table": return forEntry ? Slot.Table : Refuse();
                case "field": return forEntry ? Slot.Field : Refuse();
                case "location": return forEntry ? Slot.Location : Refuse();
                case "index": return forEntry ? Slot.Index : Refuse();

                // Both describe the file rather than one string, so both are in scope
                // wherever a pattern is written.
                case "group": return Slot.Group;
                case "namespace": return Slot.Namespace;

                // The one that only makes sense once the file is known in full, which is what
                // a header and a footer are written against.
                case "count": return forEntry ? Refuse() : Slot.Count;
            }

            throw new TabbitException(
                $"The `text` target's `{setting}` uses `{{{name}}}`, which is not a name this "
                + $"target fills in: `{pattern}`.\n"
                + $"  Per string: {{text}} {{raw}} {{table}} {{field}} {{location}} {{index}}\n"
                + $"  Per file:   {{group}} {{namespace}} {{count}}\n"
                + $"  `{{{{{{{{` writes a literal brace.");

            Slot Refuse()
            {
                throw new TabbitException(
                    forEntry
                        ? $"The `text` target's `{setting}` uses `{{{name}}}`, which describes the "
                          + $"file rather than one string, and this pattern is written once per "
                          + $"string: `{pattern}`."
                        : $"The `text` target's `{setting}` uses `{{{name}}}`, which describes one "
                          + $"gathered string. A header and a footer are written once for the "
                          + $"whole file, so there is no string for it to name: `{pattern}`.");
            }
        }

        public void Render(StringBuilder into, Entry? entry, Group group, int index)
        {
            foreach (var (slot, literal) in _parts)
            {
                switch (slot)
                {
                    case Slot.Literal: into.Append(literal); break;
                    case Slot.Text: into.Append(entry!.Text); break;
                    case Slot.Raw: into.Append(entry!.Raw); break;
                    case Slot.Table: into.Append(entry!.Table); break;
                    case Slot.Field: into.Append(entry!.Field); break;
                    case Slot.Location: into.Append(entry!.Location); break;
                    case Slot.Group: into.Append(group.Name); break;

                    // Per file, not per string - a namespace holds groups rather than sitting
                    // inside one - so a header may use it as freely as an entry line.
                    case Slot.Namespace: into.Append(group.Namespace ?? ""); break;

                    case Slot.Index:
                        into.Append(index.ToString(CultureInfo.InvariantCulture));
                        break;

                    case Slot.Count:
                        into.Append(group.Entries.Count.ToString(CultureInfo.InvariantCulture));
                        break;
                }
            }
        }
    }


    // ------------------------------------------------------------------ the template route

    /// <summary>
    /// Whether the recipe named a template this tool ships or a file of the project's own.
    /// </summary>
    /// <remarks>
    /// Both marks are checked because either alone reads as deliberate: a bare `my-format.sbn`
    /// beside the recipe, and `templates/textset` in a project that drops the extension.
    /// </remarks>
    private static bool IsPath(string name)
        => name.EndsWith(".sbn", StringComparison.OrdinalIgnoreCase)
           || name.IndexOf('/') >= 0
           || name.IndexOf('\\') >= 0;

    /// <summary>What a template parse error is reported against.</summary>
    private static string? TemplateNameOf(string named)
    {
        string name = named.Trim();
        return IsPath(name) ? name : name + ".sbn";
    }

    /// <summary>
    /// Reads the template the recipe named: one of the embedded ones, or a project's own file.
    /// </summary>
    private static string? LoadTemplate(string named)
    {
        string name = named.Trim();

        if (!IsPath(name))
            return TemplateEngine.Load(TemplateNameOf(name)!);

        if (!File.Exists(name))
        {
            throw new TabbitException(
                $"The `text` target's template `{name}` is not there. The path is read relative "
                + $"to the working directory, which is `{Directory.GetCurrentDirectory()}`.");
        }

        return File.ReadAllText(name);
    }

    /// <summary>
    /// The line ending to write, or a refusal naming what was written.
    /// </summary>
    private static string ResolveNewline(TextRecipe recipe)
    {
        switch ((recipe.LineEnding ?? "").Trim().ToLowerInvariant())
        {
            case "":
            case "lf": return "\n";
            case "crlf": return "\r\n";
        }

        throw new TabbitException(
            $"The `text` target's `LineEnding` is `{recipe.LineEnding}`. It has to be `lf` or "
            + $"`crlf`.");
    }
}
