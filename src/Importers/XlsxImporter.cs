using Tabbit.Models.Raw;
using System.IO;
using System.Collections.Generic;
using Tabbit.Models;
using System;
using Tabbit.Recipe;
using System.Linq;
using Tabbit.Sources;
using Tabbit.Cooking.Layouts;
using Tabbit.Helpers;
using Tabbit.Importers.Xlsx;
using Serilog;
using Tabbit.Messages;

namespace Tabbit.Importers;

[TabbitSource("xlsx", "Sources.Xlsx", Order = 10)]
public class XlsxImporter : Source<RecipeModel.SourceRecipeGroup.XlsxRecipe>
{
    /// <summary>Which step of a run this class's log lines belong to.</summary>
    private static Serilog.ILogger Log => LogCategory.Importing;

    private SheetImportSettings _settings = null!;

    /// <summary>
    /// One workbook being read, and everything the reading of it needs to remember.
    /// </summary>
    /// <remarks>
    /// **A class rather than a run of fields on the importer, because the workbooks are read
    /// at the same time.** Every one of these used to be instance state - which workbook,
    /// which sheet, which defined names - and that is exactly the state that cannot be shared
    /// once two files are open at once. Held here, a read is a thing with no reach outside
    /// itself, and what makes the import parallel is that there is nothing left to share.
    ///
    /// The sheets it finds are kept locally too, rather than appended to the model as they
    /// arrive. The model's sheet order decides the order tables are cooked and therefore
    /// reported in, so it is put back in the order the recipe listed the workbooks -
    /// see <see cref="Import"/>. spec/conversion-time.md section 5.
    /// </remarks>
    private sealed class WorkbookRead
    {
        /// <summary>The file as the filesystem names it, for opening it.</summary>
        public required string Path { get; init; }

        /// <summary>The same file with `/` separators, for every Location built from it.</summary>
        public required string Filename { get; init; }

        /// <summary>
        /// The workbook as the recipe names it: its path relative to the directory being
        /// searched. What a workbook pattern is matched against.
        /// </summary>
        public required string Workbook { get; init; }

        /// <summary>
        /// This workbook's defined names, or null when the layout does not use them.
        /// </summary>
        public List<WorkbookPackage.DefinedName>? Names { get; set; }

        /// <summary>What this workbook yielded, in the order its sheets were read.</summary>
        public List<RawSheet> Sheets { get; } = [];

        /// <summary>Every sheet it held, whether or not the recipe asked for it.</summary>
        public List<(string Workbook, string Sheet)> SheetsSeen { get; } = [];
    }

    /// <summary>
    /// Every workbook the directory held, so an unmatched `IncludeWorkbooks` entry can be
    /// answered with what was actually there. Recorded before the filter runs, so the list
    /// is what is on disk rather than what was read.
    /// </summary>
    private readonly List<string> _workbooksSeen = [];

    /// <summary>
    /// Every sheet the workbooks held, with the workbook it was in, so an unmatched
    /// `IncludeSheets` entry can be answered with what was actually there - and a pattern
    /// that named the wrong workbook can be answered with the right one.
    /// </summary>
    private readonly List<(string Workbook, string Sheet)> _sheetsSeen = [];

    protected override void Import(SourceContext context, RecipeModel.SourceRecipeGroup.XlsxRecipe xlsx)
    {
        // An entry with either field left blank is treated as switched off, which is how
        // an entry is commented out in practice: its contents are removed but the object
        // stays in the list.
        if (string.IsNullOrEmpty(xlsx.FileExtensionPatterns) ||
            string.IsNullOrEmpty(xlsx.Path))
        {
            return;
        }

        _settings = SheetImportSettings.From(xlsx, context.Section);
        _workbooksSeen.Clear();
        _sheetsSeen.Clear();

        var extensions = SourceFiles.Extensions(xlsx.FileExtensionPatterns);

        if (!Directory.Exists(xlsx.Path))
        {
            throw new TabbitException(null,
                Message.Of(ImportMessages.WorkbookPathMissing,
                    ("Section", context.Section), ("Path", xlsx.Path)));
        }

        // Which files are candidates is decided in one place, because the build cache asks
        // the same question on a later run to find out whether a workbook was added. Two
        // answers to it would differ exactly where it matters.
        var candidates = SourceFiles.Candidates(
            xlsx.Path,
            extensions,
            lockFile => Log.Debug($"Skipping `{lockFile}`: an Excel lock file, not a workbook."))
            .ToList();

        // The listing rather than the files: adding a workbook changes no existing file, so
        // nothing else in the ledger would notice one appearing. Recorded before the
        // recipe's own include and exclude lists are applied, so a workbook that arrives
        // while excluded is still noticed - the recipe may stop excluding it tomorrow.
        context.Inputs.Listed(
            xlsx.Path, xlsx.FileExtensionPatterns, candidates.Select(candidate => candidate.Name));

        // Which files this entry will actually open, decided before any of them is.
        //
        // Kept as a list rather than read as it is decided, because the reading below happens
        // out of order and this is what puts the results back in. The ledger and the seen-list
        // are filled here for the same reason: they are the recipe's own order, and what a run
        // says it looked at should not depend on which file finished first.
        var opening = new List<WorkbookRead>();

        foreach (var (filename, workbook) in candidates)
        {
            _workbooksSeen.Add(workbook);

            // Asked before the file is opened. A workbook that is not input costs nothing to
            // decline here, and one of a real project is 80 MB of nothing this run wants.
            if (!_settings.Filter.IncludesWorkbook(workbook))
            {
                Log.Information($"Skipping workbook `{workbook}`: the recipe does not ask for it.");
                continue;
            }

            context.Inputs.Read(filename);

            // Normalized once, here, so every Location built from this workbook shares it.
            //
            // `Location.Filename` replaces `\` with `/` on the way in, and .NET's own
            // directory walk hands us paths that still have one - so that setter allocated a
            // copy of the path per cell. One workbook of six million cells was holding six
            // million copies of the same string: 1.6 GB, all of it the same hundred
            // characters.
            opening.Add(new WorkbookRead
            {
                Path = filename,
                Filename = filename.Replace('\\', '/'),
                Workbook = workbook,
            });
        }

        // **Read at the same time.** A workbook is an independent file - inflated, parsed and
        // turned into a cell grid without reference to any other - and that is most of what
        // the import spends its time on. spec/conversion-time.md section 5.
        //
        // What each read produces goes into its own object, and those are folded into the
        // model below in the order the recipe listed them. So the model is the same whatever
        // order the files came back in, which is what keeps this a refactoring: the sheet
        // order decides which table is cooked first and therefore reported first.
        if (opening.Count > 1)
        {
            System.Threading.Tasks.Parallel.ForEach(opening, ImportWorkbook);
        }
        else
        {
            foreach (var read in opening)
                ImportWorkbook(read);
        }

        foreach (var read in opening)
        {
            _sheetsSeen.AddRange(read.SheetsSeen);
            context.Model.Sheets.AddRange(read.Sheets);
        }

        _settings.Filter.ReportUnmatchedIncludes(context.Section, _workbooksSeen, _sheetsSeen);

    }

    /// <summary>
    /// Names Excel maintains for itself, which are not tables however a layout reads them.
    /// </summary>
    /// <remarks>
    /// `_xlnm.*` is the built-in family - print areas, the autofilter's range - and `_xlfn.*`
    /// marks a function the file was written with. A leading `!_` is how a sheet-scoped name
    /// arrives spelled in some tools. The same three the project whose layout this serves
    /// filters, so the two agree about what a table is.
    /// </remarks>
    private static readonly string[] ReservedNameMarkers = ["_xlnm", "_xlfn", "!_"];

    /// <summary>Whether a defined name is a candidate for being a table at all.</summary>
    private static bool IsCandidateName(string name)
        => !ReservedNameMarkers.Any(marker => name.Contains(marker, StringComparison.Ordinal));

    private void ImportWorkbook(WorkbookRead read)
    {
        string path = read.Path;
        string filename = read.Filename;

        // A layout that finds its tables by defined name is the only one that pays for
        // resolving them, because parsing a reference means resolving it and a workbook can
        // hold hundreds.
        bool usesNames = LayoutRegistry.UsesNamedRanges(_settings.Layout.Id);

        var package = WorkbookPackage.Read(path!, usesNames ? IsCandidateName : null);
        read.Names = usesNames ? package.DefinedNames : null;

        foreach (var skipped in package.SkippedNames)
        {
            // A name whose target was deleted, or one that is not a single rectangle. Worth
            // saying so rather than dropping in silence: in real workbooks these are
            // leftovers, and one of them being a table nobody exports any more is a thing
            // to know.
            Log.Warning(Message.Of(
                skipped.Problem == WorkbookPackage.NameProblem.NotARange
                    ? ImportMessages.LogDefinedNameNotARange
                    : ImportMessages.LogDefinedNameNotReadable,
                ("Name", skipped.Name), ("File", filename),
                ("Range", skipped.Reference)).In(MessageCatalog.Current));
        }

        if (package.HasUnreadNotes)
        {
            // Cell notes are not read out of a binary workbook. Said aloud because the
            // notes become doc comments, and a workbook converted to `.xlsb` would lose
            // them with nothing else changing.
            Log.Warning(Message.Of(ImportMessages.LogBinaryWorkbookNoNotes,
                ("File", filename)).In(MessageCatalog.Current));
        }

        using var reader = SheetGridReader.Open(path);

        while (reader.MoveToNextSheet())
        {
            // By name, before any of the sheet's rows. The reader parses a sheet only as it
            // is read, so a sheet declined here costs nothing at all.
            //
            // One workbook of a real project holds a working sheet 1,816 rows by 16,381
            // columns that no defined name covers - four fifths of everything in the file.
            var sheetName = reader.SheetName.Trim();

            if (sheetName.StartsWith("#") || sheetName.StartsWith("//"))
                continue;

            read.SheetsSeen.Add((read.Workbook, sheetName));

            if (!_settings.Filter.Includes(read.Workbook, sheetName))
            {
                Log.Information($"Skipping sheet `{sheetName}` of `{filename}`: the recipe does not ask for it.");
                continue;
            }

            // A layout that finds its tables by defined name reads nothing from a sheet no
            // name covers, and says so and moves on.
            if (read.Names is not null && !CoveredByAName(read, sheetName))
            {
                Log.Information(
                    $"Skipping sheet `{sheetName}` of `{filename}`: no defined name covers it.");
                continue;
            }

            ImportSheet(read, reader, package, sheetName);
        }
    }

    private void ImportSheet(
        WorkbookRead read, SheetGridReader reader, WorkbookPackage package, string sheetName)
    {
        string filename = read.Filename;

        RawSheet rawSheet = new RawSheet
        {
            Layout = _settings.Layout,
            Location = new Location
            {
                Filename = filename,
                Sheet = sheetName,
                Column = 0,
                Row = 0
            }
        };

        // Only asked when the workbook has any, so a workbook without notes pays nothing
        // per cell for the lookup.
        bool hasNotes = package.HasNotes;

        // Where this sheet's tables are, when the layout says a table is a defined name. A
        // cell outside all of them is never read as data, so it is not this run's to report:
        // it is a working cell of a sheet that also carries a table. In the sample set that
        // is most of them - 14,199 of 24,457 formula errors, and 13,555 in one workbook.
        var rectangles = read.Names is null
            ? null
            : read.Names
                .Where(name => string.Equals(name.SheetName, sheetName, StringComparison.Ordinal))
                .ToList();

        bool firstRow = true;

        while (reader.ReadRow())
        {
            int rowIndex = reader.RowIndex;

            // Where the sheet starts, which is the first row that arrived. Set here rather
            // than up front because a streaming reader does not know it until then.
            if (firstRow)
            {
                rawSheet.Location.Row = rowIndex;
                firstRow = false;
            }

            int columnCount = reader.ColumnCount;

            List<RawCell> rawRow = new(columnCount);
            for (int colIndex = 0; colIndex < columnCount; colIndex++)
            {
                var location = new Location
                {
                    Filename = filename,
                    Sheet = sheetName,
                    Column = colIndex,
                    Row = rowIndex
                };

                string value;
                string formulaError = "";

                if (reader.IsFormulaError(colIndex, out string excelText))
                {
                    // Recorded and not reported. A cell outside every rectangle is nothing to
                    // anybody; a cell inside one is only something if the layout turns that
                    // column into a field, and that has not been decided yet.
                    // spec/formula-errors.md.
                    value = "";
                    formulaError = InsideATable(rectangles, rowIndex, colIndex) ? excelText : "";
                }
                else
                {
                    value = reader.Text(colIndex);
                }

                rawRow.Add(new RawCell
                {
                    Location = location,
                    Value = value,
                    FormulaError = formulaError,
                    Note = hasNotes ? package.Note(sheetName, rowIndex, colIndex) : ""
                });
            }

            rawSheet.Rows.Add(rawRow);
        }

        FillRecoveredCells(reader, rawSheet, filename, sheetName);

        if (!rawSheet.Optimize())
            return;


        AttachNamedRanges(read, rawSheet, sheetName);

        read.Sheets.Add(rawSheet);
    }

    /// <summary>
    /// Puts back the cells the workbook reader dropped, once the sheet has been read.
    /// </summary>
    /// <remarks>
    /// The reader hands some rows of a binary workbook shorter than the file says they are,
    /// and asking for the missing columns returns an empty value rather than an error - so
    /// without this the cells are lost in a way nothing downstream can see. Which rows were
    /// short is only known once the last of them has arrived, which is why this runs here
    /// rather than per cell.
    ///
    /// Said aloud when it happens. A silent correction would take the fact that the reader
    /// has this defect out of the run, and that fact is what decides whether a workbook can
    /// be trusted to convert. spec/xlsb-short-row-repair.md.
    /// </remarks>
    private void FillRecoveredCells(
        SheetGridReader reader, RawSheet rawSheet, string filename, string sheetName)
    {
        var recovered = reader.RecoveredCells();
        if (recovered.Count == 0)
            return;

        var rows = new HashSet<int>();

        foreach (var row in rawSheet.Rows)
        {
            foreach (var cell in row)
            {
                if (cell.Value.Length > 0)
                    continue;

                if (!recovered.TryGetValue((cell.Location.Row, cell.Location.Column), out string? text))
                    continue;

                cell.Value = text;
                rows.Add(cell.Location.Row);
            }
        }

        if (rows.Count == 0)
            return;

        Log.Warning(Message.Of(ImportMessages.LogRowsReadShort,
            ("File", filename), ("Sheet", sheetName), ("Rows", rows.Count),
            ("Recovered", recovered.Count)).In(MessageCatalog.Current));
    }

    /// <summary>
    /// Whether a cell is inside one of the rectangles this sheet's tables occupy.
    /// </summary>
    /// <remarks>
    /// True for every cell when the layout does not read defined names, because then the
    /// whole sheet is what a table is found in and there is no outside to be in.
    /// </remarks>
    private static bool InsideATable(
        List<WorkbookPackage.DefinedName>? rectangles, int row, int column)
    {
        if (rectangles is null)
            return true;

        foreach (var rectangle in rectangles)
        {
            if (row >= rectangle.FirstRow && row <= rectangle.LastRow
                && column >= rectangle.FirstColumn && column <= rectangle.LastColumn)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether any of the workbook's defined names points into this sheet.</summary>
    private static bool CoveredByAName(WorkbookRead read, string sheetName)
        => read.Names!.Any(
            name => string.Equals(name.SheetName, sheetName, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The workbook's defined names that point into this sheet, handed to
    /// <see cref="SheetNamedRanges"/> for translation onto the grid.
    /// </summary>
    /// <remarks>
    /// Attached here because the importer is the only place that knows about names: by the
    /// time the cooker runs there is a cell grid and nothing to ask about names. Only for the
    /// layouts that use them - every other sheet gets an empty list and pays nothing.
    ///
    /// What is done here is the part a workbook does differently from any other source:
    /// picking out the names that point into this sheet, which a workbook says by naming the
    /// sheet. Everything after that is shared.
    /// </remarks>
    private void AttachNamedRanges(WorkbookRead read, RawSheet rawSheet, string sheetName)
    {
        if (read.Names is null || read.Names.Count == 0)
            return;

        var forSheet = new List<SheetNamedRange>();

        foreach (var named in read.Names)
        {
            if (!string.Equals(named.SheetName, sheetName, StringComparison.Ordinal))
                continue;

            forSheet.Add(new SheetNamedRange(
                Name: named.Name,
                Reference: named.Reference,
                FirstRow: named.FirstRow,
                FirstColumn: named.FirstColumn,
                LastRow: named.LastRow,
                LastColumn: named.LastColumn));
        }

        SheetNamedRanges.Attach(
            rawSheet, forSheet, _settings.Filter, read.Workbook, read.Filename);
    }

    /// <summary>
    /// What a cell holding a formula error becomes, which the source entry decides.
    /// </summary>
    /// <remarks>
    /// Refusing is the default and the right answer for sheets the converting team can fix -
    /// a `#REF!` reaching the game as a value is the whole point of checking. `empty` exists
    /// for workbooks somebody else maintains, where one broken formula in a column nothing
    /// reads would otherwise refuse every table in the file.
    ///
    /// Warned every time rather than counted quietly, so the run says how many there were
    /// and where, and that can go back to whoever owns the sheet.
    /// </remarks>
}
