using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Tabbit.Messages;

namespace Tabbit.Reporting;

/// <summary>
/// The half of a report a person reads.
/// </summary>
/// <remarks>
/// **One list at a time.** Problems, the written-down ones, what has been fixed and what was
/// checked are four different questions, and stacking them down one page means the reader
/// scrolls past three of them to reach the one they came for. They are tabs.
///
/// **A row is one line until it is asked to be more.** A report is a paragraph - it says what
/// is wrong, what follows from it, and what to do - and fourteen paragraphs is a wall in
/// which the part that differs between them is buried. What differs is at the front of the
/// sentence, so every row is clamped to one line and opens on click: the page is scanned
/// first and read second.
///
/// **Grouped, not listed - and not grouped into groups of one.** A run has produced 5,831
/// reports. They sit under the workbook and sheet they came from, which is the order the work
/// is done in; a place holding a single report is written as a row rather than as a fold over
/// one thing. The other axis - by kind - is a click away, because a page where one kind
/// accounts for everything is read that way instead.
///
/// **The cell is a link where a link exists.** That is the difference between a report and a
/// log: for a hosted document the location is already a deep link to the cell, so fixing a
/// problem is a click. For a workbook on disk there is no portable url that opens a cell, and
/// this offers the text and a copy button rather than a link that does nothing.
///
/// **Nothing is fetched.** One file, styles and script inline, the same closed-network rule
/// the generated documentation pages keep - and the same colour tokens, so the two look like
/// output of one tool.
///
/// spec/ops/build-report.md §4.
/// </remarks>
internal static class ReportHtml
{
    /// <summary>Above this many groups, none of them start open.</summary>
    /// <remarks>
    /// A page that opens forty groups is the flat list again. Below the threshold the
    /// grouping is a heading rather than a fold, and folding it would cost a click to see
    /// what is plainly there.
    /// </remarks>
    private const int GroupsBeforeFolding = 12;

    public static string Render(
        ReportDocument document, MessageCatalog catalog, int maxEntries, string jsonName)
    {
        var page = new StringBuilder(64 * 1024);

        _root = Root(document);

        var shown = Shown(document, maxEntries, out int total);

        var problems = shown.Where(entry => entry.Severity != "info").ToList();
        var notes = shown.Where(entry => entry.Severity == "info").ToList();

        string title = Say(catalog, ReportMessages.PageTitle,
            ("Recipe", System.IO.Path.GetFileName(document.Recipe)));

        Head(page, title, catalog);
        Header(page, document, catalog);

        Tabs(page, catalog, problems.Count, document.KnownProblems.Count,
            document.Resolved.Count, notes.Count);

        Toolbar(page, catalog);

        page.Append("<main id=\"scroll\">");

        if (document.Defect is not null)
            Defect(page, document.Defect, catalog);

        if (shown.Count < total)
        {
            Note(page, Say(catalog, ReportMessages.Truncated,
                ("Shown", shown.Count.ToString("N0", CultureInfo.InvariantCulture)),
                ("Total", total.ToString("N0", CultureInfo.InvariantCulture)),
                ("File", jsonName)));
        }

        if (!document.Counts.Compared)
            Note(page, Text(catalog, ReportMessages.Uncompared));

        Problems(page, problems, catalog);

        Panel(page, "known", document.KnownProblems, catalog);
        Panel(page, "resolved", document.Resolved, catalog);
        Panel(page, "notes", notes, catalog);

        page.Append("<p class=\"empty\" id=\"nothing\" hidden>")
            .Append(Escaped(Text(catalog, ReportMessages.NothingMatches)))
            .Append("</p>");

        page.Append("</main>");

        Script(page);

        page.Append("</body></html>");

        return page.ToString();
    }

    // -------------------------------------------------------------- what fits

    /// <summary>
    /// The reports that go on the page, worst first, up to the limit.
    /// </summary>
    /// <remarks>
    /// Ordered by severity before it is cut, so a limit reached in a run with thousands of
    /// notes still shows every error. What is cut is said on the page and never cut from the
    /// JSON: the limit is about what one file can be opened with, not about what was found.
    /// </remarks>
    private static List<ReportEntry> Shown(ReportDocument document, int maxEntries, out int total)
    {
        var ordered = document.Entries
            .OrderBy(entry => entry.Severity switch { "error" => 0, "warning" => 1, _ => 2 })
            .ToList();

        total = ordered.Count;

        return maxEntries > 0 && ordered.Count > maxEntries
            ? ordered.Take(maxEntries).ToList()
            : ordered;
    }

    // ------------------------------------------------------------------ pieces

    private static void Head(StringBuilder page, string title, MessageCatalog catalog)
    {
        page.Append("<!-- Generated by Tabbit - DO NOT EDIT. -->\n")
            .Append("<html lang=\"").Append(Attribute(catalog.Language)).Append("\">\n")
            .Append("<head>\n")
            .Append("<meta http-equiv=\"Content-Type\" content=\"text/html; charset=UTF-8\">\n")
            .Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\n")
            .Append("<title>").Append(Escaped(title)).Append("</title>\n")
            .Append("<style>").Append(Style).Append("</style>\n")

            // Applied before the first paint. Applying it after is a flash of the other
            // theme on every open, which is the whole reason this sits in the head.
            .Append("<script>try{var t=localStorage.getItem('tabbit-theme');")
            .Append("if(t)document.documentElement.setAttribute('data-theme',t);}catch(e){}</script>\n")
            .Append("</head>\n<body>");
    }

    private static void Header(StringBuilder page, ReportDocument document, MessageCatalog catalog)
    {
        string outcome = document.Outcome switch
        {
            ReportOutcome.Success => Text(catalog, ReportMessages.OutcomeSuccess),
            ReportOutcome.NothingToDo => Text(catalog, ReportMessages.OutcomeNothingToDo),
            ReportOutcome.StoppedByValidation => Text(catalog, ReportMessages.OutcomeStopped),
            _ => Text(catalog, ReportMessages.OutcomeFailed),
        };

        // Three tones rather than two. A run that finished with warnings is not a run that
        // failed, and colouring it red teaches the reader that red does not mean stopped -
        // after which the red on a run that did stop says nothing.
        string tone = document.Outcome is ReportOutcome.Success or ReportOutcome.NothingToDo
            ? (document.Counts.Warnings > 0 ? "warn" : "good")
            : "bad";

        page.Append("<header class=\"bar ").Append(tone).Append("\">")
            .Append("<h1>").Append(Escaped(outcome)).Append("</h1>");

        if (!string.IsNullOrEmpty(document.Failure))
            page.Append("<p class=\"failure\">").Append(Escaped(document.Failure!)).Append("</p>");

        page.Append("<div class=\"counts\">");

        Chip(page, "error", Text(catalog, ReportMessages.LabelErrors), document.Counts.Errors);
        Chip(page, "warning", Text(catalog, ReportMessages.LabelWarnings), document.Counts.Warnings);

        // The three that answer "is this piling up". Absent on a first run, where they would
        // all read zero and mean nothing.
        if (document.Counts.Compared)
        {
            Chip(page, "new", Text(catalog, ReportMessages.LabelNew), document.Counts.New);
            Chip(page, "persisting", Text(catalog, ReportMessages.LabelPersisting),
                document.Counts.Persisting);
            Chip(page, "resolved", Text(catalog, ReportMessages.LabelResolved),
                document.Counts.Resolved);
        }

        // One line rather than a four-row table. What it says is read once, when the reader
        // checks they are looking at the right run, and four lines of it push the problems
        // themselves off the first screen.
        page.Append("</div><p class=\"meta\">");

        Meta(page, Text(catalog, ReportMessages.MetaRecipe), document.Recipe, first: true);

        // Said once here because it was dropped from every row.
        if (_root.Length > 0)
            Meta(page, Text(catalog, ReportMessages.MetaSheets), _root, first: false);

        Meta(page, Text(catalog, ReportMessages.MetaTool), document.Tool, first: false);
        Meta(page, Text(catalog, ReportMessages.MetaStarted),
            document.Started?.ToString("yyyy-MM-dd HH:mm:ss") ?? document.StartedAt, first: false);
        Meta(page, Text(catalog, ReportMessages.MetaElapsed),
            document.Elapsed.ToString("0.00", CultureInfo.InvariantCulture) + " s", first: false);

        page.Append("</p></header>");
    }

    private static void Chip(StringBuilder page, string kind, string label, int count)
        => page.Append("<span class=\"chip ").Append(kind).Append(count == 0 ? " zero" : "")
               .Append("\"><b>").Append(count.ToString("N0", CultureInfo.InvariantCulture))
               .Append("</b> ").Append(Escaped(label)).Append("</span>");

    /// <remarks>
    /// The spaces around the separator are written rather than left to a margin. A margin is
    /// not a character, so a line laid out with one copies as `recipe.jsonc·Tabbit 0.0.0` -
    /// and this line exists to be pasted into a message saying which run it was.
    /// </remarks>
    private static void Meta(StringBuilder page, string label, string value, bool first)
        => page.Append(first ? "" : "<span class=\"dot\"> · </span>")
               .Append("<span class=\"k\">").Append(Escaped(label)).Append("</span> ")
               .Append(Escaped(value));

    /// <summary>
    /// The four lists, as tabs rather than as one page.
    /// </summary>
    /// <remarks>
    /// A list that is empty still gets its tab, dimmed. Removing it would move the others
    /// about between runs, and a control that is somewhere else every time is one nobody
    /// learns the position of.
    /// </remarks>
    private static void Tabs(
        StringBuilder page, MessageCatalog catalog, int problems, int known, int resolved, int notes)
    {
        page.Append("<nav class=\"tabs\">");

        Tab(page, "problems", Text(catalog, ReportMessages.SectionProblems), problems, first: true);
        Tab(page, "known", Text(catalog, ReportMessages.SectionKnown), known, first: false);
        Tab(page, "resolved", Text(catalog, ReportMessages.SectionResolved), resolved, first: false);
        Tab(page, "notes", Text(catalog, ReportMessages.SectionNotes), notes, first: false);

        page.Append("</nav>");
    }

    private static void Tab(StringBuilder page, string name, string label, int count, bool first)
        => page.Append("<button type=\"button\" class=\"tab").Append(first ? " on" : "")
               .Append(count == 0 ? " zero" : "").Append("\" data-tab=\"").Append(name)
               .Append("\" onclick=\"tab('").Append(name).Append("')\">")
               .Append(Escaped(label)).Append("<span class=\"n\">")
               .Append(count.ToString("N0", CultureInfo.InvariantCulture))
               .Append("</span></button>");

    private static void Toolbar(StringBuilder page, MessageCatalog catalog)
    {
        string looking = Text(catalog, ReportMessages.SearchPlaceholder);

        // The clear button is written rather than left to the browser. The native one is
        // Chromium's alone - Firefox draws nothing - so a field that could be emptied with a
        // click on one machine could not on the next.
        page.Append("<div class=\"tools\">")
            .Append("<span class=\"find\">")
            .Append("<input id=\"q\" type=\"search\" placeholder=\"").Append(Attribute(looking))
            .Append("\" oninput=\"filter()\">")
            .Append("<button type=\"button\" class=\"clear\" id=\"clear\" hidden")
            .Append(" aria-label=\"").Append(Attribute(looking))
            .Append("\" onclick=\"unfind()\">&#215;</button>")
            .Append("</span>");

        Toggle(page, "error", Text(catalog, ReportMessages.LabelErrors));
        Toggle(page, "warning", Text(catalog, ReportMessages.LabelWarnings));

        page.Append("<span class=\"spacer\"></span>")
            .Append("<label class=\"by\">").Append(Escaped(Text(catalog, ReportMessages.GroupBy)))
            .Append(" <select id=\"by\" onchange=\"group(this.value)\">")
            .Append("<option value=\"sheet\">")
            .Append(Escaped(Text(catalog, ReportMessages.GroupBySheet))).Append("</option>")
            .Append("<option value=\"kind\">")
            .Append(Escaped(Text(catalog, ReportMessages.GroupByKind))).Append("</option>")
            .Append("</select></label>")
            .Append("<button type=\"button\" onclick=\"fold(true)\">")
            .Append(Escaped(Text(catalog, ReportMessages.ExpandAll))).Append("</button>")
            .Append("<button type=\"button\" onclick=\"fold(false)\">")
            .Append(Escaped(Text(catalog, ReportMessages.CollapseAll))).Append("</button>")
            .Append("<button type=\"button\" id=\"theme\" onclick=\"theme()\">◑</button>")
            .Append("</div>");
    }

    private static void Toggle(StringBuilder page, string severity, string label)
        => page.Append("<label class=\"toggle ").Append(severity)
               .Append("\"><input type=\"checkbox\" checked data-sev=\"").Append(severity)
               .Append("\" onchange=\"filter()\">").Append(Escaped(label)).Append("</label>");

    /// <summary>The problems, under the workbook and sheet each came from.</summary>
    private static void Problems(
        StringBuilder page, IReadOnlyList<ReportEntry> problems, MessageCatalog catalog)
    {
        page.Append("<section data-panel=\"problems\">");

        if (problems.Count == 0)
        {
            page.Append("<p class=\"empty\">")
                .Append(Escaped(Text(catalog, ReportMessages.NoProblems))).Append("</p>")
                .Append("</section>");

            return;
        }

        string run = Text(catalog, ReportMessages.GroupRun);

        // Groups that hold an error come first, and within each band the workbooks stay in
        // their own order. Ordering purely by severity scatters one workbook's sheets down
        // the page, which costs the reader the one thing grouping was for; ordering purely
        // by name buries the errors under whichever workbook is called `a`.
        //
        // The reports about the run itself go last however bad they are. They name no sheet,
        // so they are not somewhere anybody can be sent.
        var groups = problems
            .GroupBy(entry => entry.Location is null ? run : Sheet(entry.Location))
            .OrderBy(group => group.Key == run ? 1 : 0)
            .ThenBy(group => group.Any(entry => entry.Severity == "error") ? 0 : 1)
            .ThenBy(group => group.Key, StringComparer.Ordinal)
            .ToList();

        // Which severity decides what opens: the worst one on the page. With errors present
        // the warning groups stay folded, because the errors are what has to be read first;
        // with no errors anywhere the warnings are the worst there is, and folding all of
        // them leaves a page that looks like it found nothing.
        string worst = problems.Any(entry => entry.Severity == "error") ? "error" : "warning";

        foreach (var group in groups)
        {
            // A fold over one thing is a heading with nothing under it to justify the click.
            // The row carries its whole place instead, and reads as an item of the list it
            // is in rather than as a group of one.
            if (group.Count() == 1)
            {
                Row(page, group.First(), catalog, bare: true);
                continue;
            }

            bool leads = group.Any(entry => entry.Severity == worst);
            bool open = groups.Count == 1 || (leads && groups.Count <= GroupsBeforeFolding);

            page.Append("<details class=\"grp\"").Append(open ? " open" : "").Append("><summary>")
                .Append("<span class=\"where\">").Append(Escaped(group.Key)).Append("</span>")
                .Append("<span class=\"n\">")
                .Append(group.Count().ToString("N0", CultureInfo.InvariantCulture))
                .Append("</span></summary>");

            foreach (var entry in group)
                Row(page, entry, catalog);

            page.Append("</details>");
        }

        page.Append("</section>");
    }

    /// <summary>
    /// One of the three lists that are read as lists rather than as work.
    /// </summary>
    /// <remarks>
    /// The known, the fixed and the notes answer "what is on this list", not "which sheet do
    /// I open", so they are not gathered by place - every row carries its own.
    /// </remarks>
    private static void Panel(
        StringBuilder page, string name, IReadOnlyList<ReportEntry> entries, MessageCatalog catalog)
    {
        page.Append("<section data-panel=\"").Append(name).Append("\" hidden>");

        if (entries.Count == 0)
        {
            page.Append("<p class=\"empty\">")
                .Append(Escaped(Text(catalog, ReportMessages.NothingHere))).Append("</p>");
        }

        foreach (var entry in entries)
            Row(page, entry, catalog, bare: true);

        page.Append("</section>");
    }

    /// <summary>
    /// One report: where, what, and - once it is opened - which report it is.
    /// </summary>
    /// <param name="bare">Whether this row stands on its own rather than inside a group.</param>
    /// <param name="full">
    /// Whether it names its whole place rather than just the cell. A row says the part its
    /// heading does not: under a sheet the cell is enough, under a kind - or under no heading
    /// at all - the sheet is the thing that tells one row from the next.
    /// </param>
    /// <remarks>
    /// The id is not on the closed row. It is what a build pipeline filters on and what we
    /// grep the catalog for, and repeating it down the right-hand side of a page written for
    /// whoever owns the sheet is fourteen copies of a string they cannot act on. It is on the
    /// opened row, it is the heading when the page is grouped by kind, and the search box
    /// matches it either way.
    /// </remarks>
    private static void Row(
        StringBuilder page, ReportEntry entry, MessageCatalog catalog,
        bool bare = false, bool? full = null)
    {
        bool whole = full ?? bare;

        string place = entry.Location is null ? "" : Where(entry.Location);
        string cell = entry.Location is null ? "" : Cell(entry.Location);

        string searchable = (entry.Message + " " + (entry.Id ?? "") + " " + place).ToLowerInvariant();

        page.Append("<div class=\"row ").Append(entry.Severity).Append(bare ? " bare" : "")
            .Append(whole ? " full" : "")
            .Append("\" data-sev=\"").Append(entry.Severity)
            .Append("\" data-sheet=\"")
            .Append(Attribute(entry.Location is null
                ? Text(catalog, ReportMessages.GroupRun)
                : Sheet(entry.Location)))
            .Append("\" data-kind=\"").Append(Attribute(entry.Id ?? ""))
            .Append("\" data-t=\"").Append(Attribute(searchable))
            .Append("\" onclick=\"open_(this, event)\">");

        page.Append("<span class=\"sev\"></span>");

        // What is wrong, first and in the reading face. The place is where the fix happens
        // and it is a column of its own at the other end of the row - putting it first gave
        // the most prominent position on every line to the part that repeats.
        page.Append("<span class=\"msg\">").Append(Marked(entry.Message)).Append("</span>");

        // Only what is new. "Still here" is the ordinary state of a problem, so a badge
        // saying it on every row says nothing - the count in the header is where that
        // belongs.
        if (entry.Fate == ReportFate.New)
            Badge(page, "new", Text(catalog, ReportMessages.BadgeNew));

        if (entry.Location is not null)
            Place(page, entry.Location, place, cell, whole);

        Copy(page, catalog, entry);

        if (!string.IsNullOrEmpty(entry.Id))
            page.Append("<span class=\"id\">").Append(Escaped(entry.Id!)).Append("</span>");

        page.Append("</div>");
    }

    private static void Badge(StringBuilder page, string kind, string label)
        => page.Append("<span class=\"badge ").Append(kind).Append("\">")
               .Append(Escaped(label)).Append("</span>");

    /// <summary>
    /// The cell: a link where one exists, and text with a copy button where none does.
    /// </summary>
    /// <remarks>
    /// Both spellings of the place travel with it - the cell on its own and the whole path -
    /// because regrouping the page moves a row into and out of a heading that already names
    /// its sheet, and the row has to say the part the heading does not.
    /// </remarks>
    private static void Place(
        StringBuilder page, ReportLocation location, string place, string cell, bool whole)
    {
        string shown = whole ? place : cell;

        if (!string.IsNullOrEmpty(location.Url))
        {
            page.Append("<a class=\"at\" target=\"_blank\" rel=\"noopener\" title=\"")
                .Append(Attribute(place))
                .Append("\" data-cell=\"").Append(Attribute(cell))
                .Append("\" data-full=\"").Append(Attribute(place))
                .Append("\" href=\"").Append(Attribute(location.Url)).Append("\">")
                .Append(Escaped(shown)).Append("</a>");

            return;
        }

        page.Append("<span class=\"at plain\" title=\"").Append(Attribute(place))
            .Append("\" data-cell=\"").Append(Attribute(cell))
            .Append("\" data-full=\"").Append(Attribute(place)).Append("\">")
            .Append(Escaped(shown)).Append("</span>");
    }

    /// <summary>
    /// Copies the whole report, in the shape the console prints it.
    /// </summary>
    /// <remarks>
    /// It used to copy the location and nothing else, which is not the thing anybody sends.
    /// What gets pasted into a message to whoever owns the sheet is the sentence, the place
    /// under it, and - for a hosted document - the link that opens the cell, so the person
    /// receiving it can go straight there. The id goes last, for us.
    ///
    /// The same three lines the console writes, because a report quoted in a message and the
    /// same report quoted from a log should not read as two different things.
    /// </remarks>
    private static void Copy(StringBuilder page, MessageCatalog catalog, ReportEntry entry)
    {
        var said = new StringBuilder(entry.Message);

        // The whole path, not the page's shortened one. This goes to somebody who does not
        // have the page in front of them.
        if (entry.Location is not null)
            said.Append("\n    at ").Append(Where(entry.Location, whole: true));

        if (!string.IsNullOrEmpty(entry.Location?.Url))
            said.Append("\n    ").Append(entry.Location!.Url);

        if (!string.IsNullOrEmpty(entry.Id))
            said.Append("\n    ").Append(entry.Id);

        page.Append("<button type=\"button\" class=\"copy\" title=\"")
            .Append(Attribute(Text(catalog, ReportMessages.CopyReport)))
            .Append("\" data-copy=\"").Append(Attribute(said.ToString()))
            .Append("\" data-done=\"")
            .Append(Attribute(Text(catalog, ReportMessages.Copied)))
            .Append("\" onclick=\"copy(this, event)\">")
            .Append(Escaped(Text(catalog, ReportMessages.Copy))).Append("</button>");
    }

    /// <summary>
    /// The sheet a report came from, which is what a group is.
    /// </summary>
    /// <remarks>
    /// The cell is deliberately not in it. Grouping by cell is not grouping - it is the flat
    /// list again with a heading over each row - and the reader's question is which sheet to
    /// open, not which cell.
    /// </remarks>
    private static string Sheet(ReportLocation location)
        => string.IsNullOrEmpty(location.Sheet)
            ? Shortened(location.File)
            : $"{Shortened(location.File)} : {location.Sheet}";

    /// <summary>
    /// The folder every place on this page shares, dropped from the rows and said once.
    /// </summary>
    /// <remarks>
    /// A real page had one sample's folder down the left of fourteen rows: a dozen characters
    /// of the most prominent position on every line, saying the same thing every time, in
    /// front of the part that differs.
    ///
    /// Only a whole folder is dropped, and only when more than one place shares it - cutting
    /// a common run of letters would leave names that are not names. What the copy button
    /// carries is unaffected: that goes to somebody who does not have this page in front of
    /// them.
    ///
    /// Static, and settled once at the top of a render. That holds because a run writes one
    /// report and writes it on one thread; two renders at once would read each other's. If a
    /// second caller ever appears, this becomes an argument.
    /// </remarks>
    private static string _root = "";

    /// <summary>Works out that folder, or empty when the places share none.</summary>
    private static string Root(ReportDocument document)
    {
        var files = document.Entries
            .Concat(document.KnownProblems)
            .Concat(document.Resolved)
            .Where(entry => entry.Location is not null && !entry.Location.InTextFile)
            .Select(entry => entry.Location!.File)
            .Where(file => file.Contains('/'))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (files.Count < 2)
            return "";

        string first = files[0];
        int at = first.Length;

        foreach (string file in files)
        {
            at = Math.Min(at, Shared(first, file));

            if (at == 0)
                return "";
        }

        // Back to the last separator: half a folder name is not a folder.
        int slash = first.LastIndexOf('/', Math.Max(0, at - 1));

        return slash <= 0 ? "" : first[..(slash + 1)];
    }

    private static int Shared(string one, string other)
    {
        int at = 0;

        while (at < one.Length && at < other.Length && one[at] == other[at])
            at++;

        return at;
    }

    private static string Shortened(string file)
        => _root.Length > 0 && file.StartsWith(_root, StringComparison.Ordinal)
            ? file[_root.Length..]
            : file;

    /// <summary>Just the cell, for a row sitting under a heading that names the sheet.</summary>
    private static string Cell(ReportLocation location)
        => string.IsNullOrEmpty(location.Cell) ? location.File : location.Cell;

    /// <summary>The place, written the way this tool has always written one.</summary>
    private static string Where(ReportLocation location) => Where(location, whole: false);

    private static string Where(ReportLocation location, bool whole)
    {
        string file = whole ? location.File : Shortened(location.File);

        if (location.InTextFile)
            return $"{file}{location.Cell}";

        if (string.IsNullOrEmpty(location.Sheet))
            return file;

        return $"{file} : {location.Sheet} : {location.Cell}";
    }

    private static void Defect(StringBuilder page, ReportDefect defect, MessageCatalog catalog)
    {
        page.Append("<section class=\"defect\"><h2>")
            .Append(Escaped(Text(catalog, ReportMessages.SectionDefect))).Append("</h2>")
            .Append("<p>").Append(Escaped(Text(catalog, ReportMessages.DefectNote))).Append("</p>")
            .Append("<p class=\"failure\">").Append(Escaped(defect.Message)).Append("</p>");

        if (!string.IsNullOrEmpty(defect.Stack))
            page.Append("<pre>").Append(Escaped(defect.Stack!)).Append("</pre>");

        page.Append("</section>");
    }

    private static void Note(StringBuilder page, string text)
        => page.Append("<p class=\"note\">").Append(Escaped(text)).Append("</p>");

    // ------------------------------------------------------------------ script

    private static void Script(StringBuilder page)
        => page.Append("<script>").Append(Behaviour).Append("</script>");

    // ------------------------------------------------------------------ saying

    private static string Text(MessageCatalog catalog, string id) => catalog.TextOf(id);

    private static string Say(MessageCatalog catalog, string id, params (string, object?)[] values)
        => Message.Of(id, values).In(catalog);

    private static string Escaped(string text)
    {
        var built = new StringBuilder(text.Length + 8);

        foreach (char letter in text)
        {
            switch (letter)
            {
                case '&': built.Append("&amp;"); break;
                case '<': built.Append("&lt;"); break;
                case '>': built.Append("&gt;"); break;
                case '"': built.Append("&quot;"); break;
                case '\'': built.Append("&#39;"); break;
                default: built.Append(letter); break;
            }
        }

        return built.ToString();
    }

    /// <remarks>
    /// Newlines are written as entities. What the copy button carries is three lines, and a
    /// raw newline inside an attribute survives some parsers and not others.
    /// </remarks>
    private static string Attribute(string text)
        => Escaped(text).Replace("\r\n", "&#10;").Replace("\n", "&#10;");

    /// <summary>
    /// A report, with what it quotes in backticks set as code.
    /// </summary>
    /// <remarks>
    /// Every report names things - a column, a value, a setting - and it names them the way
    /// this repository's own prose does, in backticks. On a page they were plain text, so
    /// the one part of the sentence that differs from the report above it read exactly like
    /// the part that does not.
    ///
    /// Paired, and only paired: an odd backtick left over is text. Nothing else of markdown
    /// is read - a report is a sentence this tool wrote, not a document somebody authored,
    /// and taking `*` for emphasis would eat the asterisk out of a pattern a report is
    /// quoting.
    /// </remarks>
    private static string Marked(string message)
    {
        string escaped = Escaped(message);

        var parts = escaped.Split('`');

        // No pair, nothing to set. An odd count means the last one opens nothing.
        if (parts.Length < 3)
            return escaped;

        var built = new StringBuilder(escaped.Length + 16);

        for (int at = 0; at < parts.Length; at++)
        {
            bool inside = at % 2 == 1 && at < parts.Length - 1;

            if (inside)
                built.Append("<code>").Append(parts[at]).Append("</code>");
            else if (at % 2 == 1)
                built.Append('`').Append(parts[at]);
            else
                built.Append(parts[at]);
        }

        return built.ToString();
    }

    // -------------------------------------------------------------------- look

    /// <summary>
    /// GitHub's own palette, type and components.
    /// </summary>
    /// <remarks>
    /// Primer, deliberately and closely: the colour tokens, the 14px system stack, the box
    /// with one frame and a rule between rows, the pill labels and counters, the underline
    /// nav, the button. Not because this page is on GitHub, but because the people reading it
    /// spend their day on pages that look like this - a tool's own dialect of grey boxes is a
    /// thing to learn before the content can be read.
    ///
    /// Nothing is fetched. One file, styles and script inline, the same closed-network rule
    /// the generated documentation pages keep.
    /// </remarks>
    private const string Style = """
/* ---- Primer tokens.

   Three times over: the light set on the root, the dark set behind a media query for a
   reader who has chosen no theme here, and the dark set again behind an attribute for a
   reader who has. The last is what lets the control override the system in both
   directions - and no colour has its only definition inside the media block. ---- */
:root {
  --canvas:#ffffff; --subtle:#f6f8fa; --inset:#f6f8fa;
  --border:#d1d9e0; --border-muted:#d8dee4;
  --fg:#1f2328; --fg-muted:#59636e; --fg-subtle:#818b98;
  --accent:#0969da; --danger:#d1242f; --attention:#9a6700; --success:#1a7f37; --done:#8250df;
  --neutral:rgba(129,139,152,.12); --counter:rgba(129,139,152,.2);
  --btn:#f6f8fa; --btn-hover:#eff2f5; --btn-border:rgba(31,35,40,.15);
  --btn-shadow:0 1px 0 rgba(31,35,40,.04);
  --selected:#fd8c73;
  --flash:#ddf4ff; --flash-border:rgba(84,174,255,.4);
  --danger-tint:rgba(255,129,130,.1); --attention-tint:rgba(212,167,44,.15);
  --mark:#fff8c5;
  color-scheme: light;
}
@media (prefers-color-scheme: dark) {
  :root:not([data-theme="light"]) {
    --canvas:#0d1117; --subtle:#151b23; --inset:#010409;
    --border:#3d444d; --border-muted:#2f353d;
    --fg:#f0f6fc; --fg-muted:#9198a1; --fg-subtle:#656c76;
    --accent:#4493f8; --danger:#f85149; --attention:#d29922; --success:#3fb950; --done:#ab7df8;
    --neutral:rgba(101,109,118,.2); --counter:rgba(101,109,118,.2);
    --btn:#212830; --btn-hover:#262c36; --btn-border:#3d444d;
    --btn-shadow:0 0 transparent;
    --selected:#fd8c73;
    --flash:rgba(56,139,253,.1); --flash-border:rgba(56,139,253,.4);
    --danger-tint:rgba(248,81,73,.1); --attention-tint:rgba(187,128,9,.15);
    --mark:rgba(187,128,9,.4);
    color-scheme: dark;
  }
}
:root[data-theme="dark"] {
  --canvas:#0d1117; --subtle:#151b23; --inset:#010409;
  --border:#3d444d; --border-muted:#2f353d;
  --fg:#f0f6fc; --fg-muted:#9198a1; --fg-subtle:#656c76;
  --accent:#4493f8; --danger:#f85149; --attention:#d29922; --success:#3fb950; --done:#ab7df8;
  --neutral:rgba(101,109,118,.2); --counter:rgba(101,109,118,.2);
  --btn:#212830; --btn-hover:#262c36; --btn-border:#3d444d;
  --btn-shadow:0 0 transparent;
  --selected:#fd8c73;
  --flash:rgba(56,139,253,.1); --flash-border:rgba(56,139,253,.4);
  --danger-tint:rgba(248,81,73,.1); --attention-tint:rgba(187,128,9,.15);
  --mark:rgba(187,128,9,.4);
  color-scheme: dark;
}
* { box-sizing: border-box; }

/* The filter hides rows with the `hidden` attribute, and a row is a flex box. An author's
   `display` beats the one the browser attaches to `hidden`, so without this the filter
   changes every count on the page and hides nothing - which reads as a filter that found
   more than it did. */
[hidden] { display: none !important; }

html, body { height: 100%; }
body {
  margin: 0; display: flex; flex-direction: column; overflow: hidden;
  background: var(--canvas); color: var(--fg);
  font: 14px/1.5 -apple-system, BlinkMacSystemFont, "Segoe UI", "Noto Sans", Helvetica, Arial,
        "Malgun Gothic", "Apple SD Gothic Neo", sans-serif;
}
a { color: var(--accent); text-decoration: none; }
a:hover { text-decoration: underline; }
.id, .at, code { font-family: ui-monospace, SFMono-Regular, "SF Mono", Menlo, Consolas,
                              "Liberation Mono", monospace; }
* { scrollbar-width: thin; }

/* What a report quotes - a column, a value, a setting - is the part that differs from the
   report above it, and as plain text it read like the part that does not. The chip is the
   one this page's readers already know; the colour is the palette's own `done`, because a
   tint alone was not enough to find by eye down a page of them. */
code { padding: .2em .4em; font-size: 85%; border-radius: 6px;
       background: var(--neutral); color: var(--done); }

/* One scrolling region, and the content held to a column on a wide screen. The padding
   centres it while the rules above and below still run the full width. */
header.bar, .tabs, .tools, main {
  padding-inline: max(16px, calc((100% - 1280px) / 2));
}
main { flex: 1; overflow-y: auto; padding-top: 16px; padding-bottom: 48px; }

/* ---- the subhead ---- */
header.bar { padding-top: 16px; padding-bottom: 12px;
             background: var(--canvas); border-bottom: 1px solid var(--border); }
header.bar h1 { margin: 0; font-size: 20px; font-weight: 600; line-height: 1.25; }
header.bar.bad h1 { color: var(--danger); }
header.bar.good h1 { color: var(--success); }
header.bar.warn h1 { color: var(--attention); }
.failure { margin: 6px 0 0; }

/* ---- labels ---- */
.counts { display: flex; flex-wrap: wrap; gap: 6px; margin: 10px 0 0; }
.chip { padding: 0 10px; border: 1px solid var(--border); border-radius: 2em;
        font-size: 12px; font-weight: 500; line-height: 22px; color: var(--fg-muted); }
.chip b { font-variant-numeric: tabular-nums; font-weight: 600; }
.chip.zero { color: var(--fg-subtle); }
.chip.error { color: var(--danger); border-color: var(--danger); background: var(--danger-tint); }
.chip.warning { color: var(--attention); border-color: var(--attention);
                background: var(--attention-tint); }
.chip.error.zero, .chip.warning.zero { color: var(--fg-subtle); border-color: var(--border);
                                       background: none; }
.chip.new b { color: var(--danger); }
.chip.resolved b { color: var(--success); }

p.meta { margin: 10px 0 0; font-size: 12px; color: var(--fg-muted); overflow-wrap: anywhere; }
p.meta .k { color: var(--fg-subtle); }
p.meta .dot { color: var(--fg-subtle); }

/* ---- underline nav ---- */
.tabs { display: flex; gap: 0; background: var(--canvas);
        border-bottom: 1px solid var(--border); }
.tab { appearance: none; border: 0; background: none; color: var(--fg); cursor: pointer;
       padding: 8px 16px; font: inherit; line-height: 30px;
       border-bottom: 2px solid transparent; margin-bottom: -1px; }
.tab:hover { background: var(--neutral); border-radius: 6px 6px 0 0; }
.tab.on { font-weight: 600; border-bottom-color: var(--selected); }
.tab.zero { color: var(--fg-muted); }
.tab .n { margin-left: 8px; font-size: 12px; font-weight: 500; line-height: 18px;
          padding: 0 6px; border-radius: 2em; background: var(--counter); color: var(--fg-muted);
          font-variant-numeric: tabular-nums; }

/* ---- the control strip ---- */
.tools { display: flex; align-items: center; gap: 8px; flex-wrap: wrap;
         padding-top: 12px; padding-bottom: 12px; background: var(--canvas); }
.find { position: relative; display: flex; flex: 1 1 240px; min-width: 160px; }
.tools input[type=search] {
  width: 100%; padding: 5px 30px 5px 12px; font: inherit; line-height: 20px;
  border: 1px solid var(--border); border-radius: 6px;
  background: var(--canvas); color: var(--fg); }
.tools input[type=search]::-webkit-search-cancel-button { appearance: none; display: none; }
/* The glyph and nothing else. Centred by the box rather than by a line height - a line
   height centres the line, not the mark drawn inside it, and this one sat low. */
.clear { position: absolute; right: 6px; top: 0; bottom: 0; margin: auto 0;
  display: flex; align-items: center; justify-content: center;
  width: 18px; height: 18px; padding: 0; line-height: 1;
  border: 0; background: none; color: var(--fg-subtle); font-size: 16px; cursor: pointer; }
.clear:hover { color: var(--fg); }

/* Where the search found what it was given. */
mark { background: var(--mark); color: inherit; border-radius: 3px; padding: 0 1px; }
.tools input[type=search]:focus {
  outline: none; border-color: var(--accent); box-shadow: 0 0 0 3px rgba(9,105,218,.3); }
.tools button, .tools select {
  padding: 5px 12px; font: inherit; font-size: 14px; font-weight: 500; line-height: 20px;
  border: 1px solid var(--btn-border); border-radius: 6px; box-shadow: var(--btn-shadow);
  background: var(--btn); color: var(--fg); cursor: pointer; }
.tools button:hover, .tools select:hover { background: var(--btn-hover); }
.toggle, .by { font-size: 12px; color: var(--fg-muted); user-select: none; cursor: pointer;
               white-space: nowrap; }
.toggle input { vertical-align: -2px; margin-right: 4px; accent-color: var(--accent); }
.spacer { flex: 1 1 0; }

/* ---- flash ---- */
.note { margin: 0 0 16px; padding: 12px 16px; border: 1px solid var(--flash-border);
        border-radius: 6px; background: var(--flash); font-size: 14px; color: var(--fg); }
.empty { color: var(--fg-muted); padding: 16px; margin: 0; }

/* ---- the box: one frame around the list, one rule between rows ---- */
section[data-panel] { border: 1px solid var(--border); border-radius: 6px;
                      background: var(--canvas); overflow: hidden; }
section[data-panel] > details.grp { border: 0; border-top: 1px solid var(--border);
                                    border-radius: 0; margin: 0; }
section[data-panel] > details.grp:first-child { border-top: 0; }
details.grp { border: 1px solid var(--border); border-radius: 6px; margin: 0; overflow: hidden; }
details.grp > summary { display: flex; align-items: center; gap: 8px; cursor: pointer;
  padding: 8px 16px; background: var(--subtle); font-size: 14px; font-weight: 600; }
details.grp > summary::marker { color: var(--fg-muted); }
details.grp > summary .where { overflow-wrap: anywhere; }
details.grp > summary .n { margin-left: auto; font-weight: 500; font-size: 12px;
  padding: 0 6px; border-radius: 2em; background: var(--counter); color: var(--fg-muted);
  font-variant-numeric: tabular-nums; }

/* ---- a row is one line until it is asked to be more ---- */
.row { display: flex; flex-wrap: wrap; align-items: baseline; gap: 4px 12px;
       padding: 8px 16px; border-top: 1px solid var(--border-muted); cursor: pointer; }
.row:first-child { border-top: 0; }
.row:hover { background: var(--subtle); }
.row.bare { border: 0; border-top: 1px solid var(--border-muted); margin: 0; }

/* The state dot, in the palette's own danger and attention. */
.row .sev { flex: 0 0 8px; height: 8px; border-radius: 50%; align-self: center;
            background: var(--fg-subtle); }

/* On an opened row the dot belongs beside the first line, not floating at the middle of
   however many lines the sentence turned out to be. */
.row.open .sev { align-self: flex-start; margin-top: 7px; }
.row.error .sev { background: var(--danger); }
.row.warning .sev { background: var(--attention); }

/* Basis zero, not `auto`. With `auto` the base size is the whole sentence, and a wrapping
   row then puts every message on a line of its own however wide the window is.

   This is the content, so it is the one thing at full strength: everything else on the row
   is smaller, quieter, or both. */
.msg { flex: 1 1 0; overflow-wrap: anywhere; min-width: 14rem; color: var(--fg);
       display: -webkit-box; -webkit-line-clamp: 1; -webkit-box-orient: vertical; overflow: hidden; }
.row.open .msg { display: block; }

/* Where the fix happens, at the other end of the row from what is wrong. Every row ends
   with it, so their right edges line up without a width being set - and it is smaller and
   quieter than the sentence, because it is the address rather than the message. */
.at { flex: 0 0 auto; color: var(--accent); white-space: nowrap; font-size: 12px; }
.at.plain { color: var(--fg-subtle); }
.row:hover .at.plain { color: var(--fg-muted); }

/* Off the closed row: it is what a pipeline filters on, not something the person holding the
   sheet can act on, and repeated down the page it is one string as many times as there are
   rows. On the opened row it gets a line of its own, indented to the sentence above it, so
   the opened row reads as one thing with a detail under it rather than as two rows. */
.id { display: none; color: var(--fg-subtle); font-size: 12px; }
.row.open .id { display: block; flex: 0 0 100%; margin-left: 20px; padding-top: 6px;
                white-space: nowrap; overflow-x: auto; }
.row.open { padding-bottom: 10px; }

/* Only `new` is ever written, so it is allowed to be loud. */
.badge { flex: 0 0 auto; font-size: 12px; font-weight: 500; line-height: 18px; padding: 0 7px;
         border-radius: 2em; color: var(--danger); border: 1px solid var(--danger);
         background: var(--danger-tint); }

/* On hover, and on hover only. A button beside every place is one control as many times as
   there are rows; a button that is also there when the row happens to be open is a control
   that appears for a reason the reader cannot see, which reads as arbitrary. One trigger. */
.copy { flex: 0 0 auto; padding: 0 8px; font: inherit; font-size: 12px; font-weight: 500;
        line-height: 20px; border: 1px solid var(--btn-border); border-radius: 6px;
        background: var(--btn); color: var(--fg-muted); cursor: pointer; visibility: hidden; }
.copy:hover { background: var(--btn-hover); color: var(--fg); }
.row:hover .copy { visibility: visible; }

.defect { border: 1px solid var(--danger); border-radius: 6px; padding: 16px;
          margin-bottom: 16px; background: var(--danger-tint); }
.defect h2 { margin: 0 0 8px; color: var(--danger); font-size: 16px; }
.defect pre { overflow-x: auto; font-size: 12px; color: var(--fg-muted); }
""";

    /// <summary>
    /// Tabs, folding, filtering, regrouping, copying, and the theme control.
    /// </summary>
    /// <remarks>
    /// A group whose every row is filtered out is hidden rather than left as an empty
    /// heading, and the count on each group is the number still showing - a filter that
    /// leaves the totals as they were is a filter that cannot be trusted.
    ///
    /// Regrouping moves the rows rather than re-rendering them, so nothing has to be written
    /// twice into the page: the row already carries both spellings of its place and both of
    /// the keys it can be gathered under.
    /// </remarks>
    private const string Behaviour = """
function panel() {
  return document.querySelector('section[data-panel]:not([hidden])');
}

function tab(name) {
  document.querySelectorAll('section[data-panel]').forEach(function (s) {
    s.hidden = s.dataset.panel !== name;
  });
  document.querySelectorAll('.tab').forEach(function (b) {
    b.classList.toggle('on', b.dataset.tab === name);
  });
  filter();
}

function open_(row, event) {
  // A click on the link, the copy button or a selection is not a click on the row.
  if (event.target.closest('a, button')) return;
  if (window.getSelection && String(window.getSelection())) return;
  row.classList.toggle('open');
}

function unfind() {
  var box = document.getElementById('q');
  box.value = '';
  box.focus();
  filter();
}

function filter() {
  var box = document.getElementById('q');
  var q = box.value.trim().toLowerCase();

  document.getElementById('clear').hidden = box.value.length === 0;
  var on = {};
  document.querySelectorAll('.toggle input').forEach(function (box) {
    on[box.dataset.sev] = box.checked;
  });

  var host = panel();
  if (!host) return;

  unmark();

  var any = 0;
  host.querySelectorAll('.row').forEach(function (row) {
    var sev = row.dataset.sev;
    var keep = (on[sev] !== false) && (!q || row.dataset.t.indexOf(q) >= 0);
    row.hidden = !keep;

    if (!keep) return;

    any++;

    // Only what is on screen and matching. Marking a page of five thousand rows to show a
    // dozen of them is work nobody sees.
    if (q) {
      mark(row.querySelector('.msg'), q);
      mark(row.querySelector('.at'), q);
    }
  });

  host.querySelectorAll('details.grp').forEach(function (group) {
    var showing = group.querySelectorAll('.row:not([hidden])').length;
    group.hidden = showing === 0;
    var n = group.querySelector('summary .n');
    if (n) n.textContent = showing.toLocaleString();
    if (q && showing > 0) group.open = true;
  });

  // Only when a filter is what emptied the list. A list that was empty to begin with has
  // already said so, and two lines saying the same thing is a page arguing with itself.
  var held = host.querySelectorAll('.row').length;

  document.getElementById('nothing').hidden = any > 0 || held === 0;
}

/* Both of the things that fold on this page. A page whose places mostly hold one report
   each has almost no group folds on it, and a control that only worked on those read as a
   control that did nothing. */
/* What the search found, shown where it found it.

   The text nodes are walked rather than the markup being searched and rewritten: a report
   carries `code` elements for what it quotes, and a replace over `innerHTML` would match
   inside a tag and take the page apart. Each element keeps its untouched markup so the
   marks can be lifted again on the next keystroke. */
var marked = [];

function unmark() {
  marked.forEach(function (el) { el.innerHTML = el.dataset.html; });
  marked = [];
}

function mark(el, q) {
  if (!el) return;

  if (el.dataset.html === undefined) el.dataset.html = el.innerHTML;

  var walker = document.createTreeWalker(el, NodeFilter.SHOW_TEXT, null);
  var nodes = [];
  while (walker.nextNode()) nodes.push(walker.currentNode);

  var hit = false;

  nodes.forEach(function (node) {
    var text = node.nodeValue;
    var low = text.toLowerCase();
    var at = low.indexOf(q);
    if (at < 0) return;

    var piece = document.createDocumentFragment();
    var from = 0;

    while (at >= 0) {
      piece.appendChild(document.createTextNode(text.slice(from, at)));
      var found = document.createElement('mark');
      found.textContent = text.slice(at, at + q.length);
      piece.appendChild(found);
      from = at + q.length;
      at = low.indexOf(q, from);
    }

    piece.appendChild(document.createTextNode(text.slice(from)));
    node.parentNode.replaceChild(piece, node);
    hit = true;
  });

  if (hit) marked.push(el);
}

function fold(open) {
  var host = panel();
  if (!host) return;

  host.querySelectorAll('details.grp').forEach(function (group) { group.open = open; });
  host.querySelectorAll('.row').forEach(function (row) { row.classList.toggle('open', open); });
}

/* Gathers the problems under the other axis. The rows are moved, not rebuilt: each one
   already carries the sheet it came from, the kind of report it is, and both spellings of
   its place - so a row that ends up standing on its own can say the part a heading would
   have said. */
function group(axis) {
  var host = document.querySelector('section[data-panel="problems"]');
  if (!host) return;

  var rows = Array.prototype.slice.call(host.querySelectorAll('.row'));
  if (!rows.length) return;

  var order = [], by = {};

  rows.forEach(function (row) {
    var key = axis === 'kind' ? (row.dataset.kind || '-') : row.dataset.sheet;
    if (!by[key]) { by[key] = []; order.push(key); }
    by[key].push(row);
  });

  host.textContent = '';

  order.forEach(function (key) {
    var band = by[key];

    // A row says the part its heading does not. Under a kind the sheet is what tells one
    // row from the next; under a sheet the cell is.
    var whole = axis === 'kind';

    if (band.length === 1) {
      place(band[0], true, true);
      host.appendChild(band[0]);
      return;
    }

    var box = document.createElement('details');
    box.className = 'grp';
    box.open = true;

    var head = document.createElement('summary');
    var where = document.createElement('span');
    where.className = 'where';
    where.textContent = key;
    var count = document.createElement('span');
    count.className = 'n';
    count.textContent = band.length.toLocaleString();
    head.appendChild(where);
    head.appendChild(count);
    box.appendChild(head);

    band.forEach(function (row) { place(row, false, whole); box.appendChild(row); });
    host.appendChild(box);
  });

  filter();
}

function place(row, bare, whole) {
  row.classList.toggle('bare', bare);
  row.classList.toggle('full', whole);
  var at = row.querySelector('.at');
  if (at) at.textContent = whole ? at.dataset.full : at.dataset.cell;
}

function copy(button, event) {
  event.stopPropagation();

  var text = button.dataset.copy;
  var said = button.textContent;
  var done = function () {
    button.textContent = button.dataset.done;
    setTimeout(function () { button.textContent = said; }, 1200);
  };

  if (navigator.clipboard) { navigator.clipboard.writeText(text).then(done, done); return; }

  // No clipboard api off https. A textarea and execCommand is what is left, and a report
  // opened from a file path is exactly the case that needs it.
  var box = document.createElement('textarea');
  box.value = text;
  document.body.appendChild(box);
  box.select();
  try { document.execCommand('copy'); } catch (e) {}
  document.body.removeChild(box);
  done();
}

function theme() {
  var root = document.documentElement;
  var now = root.getAttribute('data-theme');
  var next = now === 'dark' ? 'light' : (now === 'light' ? '' : 'dark');

  if (next) { root.setAttribute('data-theme', next); } else { root.removeAttribute('data-theme'); }
  try { if (next) { localStorage.setItem('tabbit-theme', next); }
        else { localStorage.removeItem('tabbit-theme'); } } catch (e) {}
}
""";
}
