using Tabbit.Models.Raw;
using System.IO;
using System.Collections.Generic;
using Tabbit.Models;
using System;
using Tabbit.Recipe;
using System.Linq;
using Tabbit.Sources;
using Tabbit.Cooking.Layouts;
using Tabbit.Importers.Xlsx;
using Serilog;

namespace Tabbit.Importers;

[TabbitSource("xlsx", "Sources.Xlsx", Order = 10)]
public class XlsxImporter : Source<RecipeModel.SourceRecipeGroup.XlsxRecipe>
{
    private RawModel _model = null!;

    private string _currentFilename = "";
    private string _currentSheetName = "";

    /// <summary>
    /// The current workbook as the recipe names it: its path relative to the directory being
    /// searched. What a workbook pattern is matched against.
    /// </summary>
    private string _currentWorkbook = "";

    private SheetImportSettings _settings = null!;

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

        _model = context.Model;
        _settings = SheetImportSettings.From(xlsx, context.Section);
        _workbooksSeen.Clear();
        _sheetsSeen.Clear();

        var fileExtensionPatterns = xlsx.FileExtensionPatterns.Split(";");
        if (fileExtensionPatterns is null || fileExtensionPatterns.Length == 0)
        {
            fileExtensionPatterns = [".xlsx"];
        }
        else
        {
            for (int i = 0; i < fileExtensionPatterns.Length; i++)
                fileExtensionPatterns[i] = fileExtensionPatterns[i].Trim().ToLowerInvariant();
        }

        if (!Directory.Exists(xlsx.Path))
        {
            throw new TabbitException(
                $"Recipe `{context.Section}` reads workbooks from `{xlsx.Path}`, which does not exist.");
        }

        // Ordered, because this is the order the tables enter the model in and so the order
        // they leave in. The filesystem's own order is not the same on ext4 as on NTFS, which
        // made the same directory of workbooks produce different output on Linux than on
        // Windows - silently, since both are valid outputs of a run that read everything.
        var files = Helpers.PathNames.InOrder(
            Directory.GetFiles(xlsx.Path, "*.*", SearchOption.AllDirectories));

        foreach (var filename in files)
        {
            if (filename.Contains("/#") || filename.Contains("\\#"))
                continue;

            // Excel's lock file for a workbook somebody has open: `~$Book.xlsx`, same
            // extension and a few hundred bytes of nothing usable. Reading one throws,
            // so leaving a workbook open in Excel used to fail the whole run - and the
            // message named a file the author never created.
            if (Path.GetFileName(filename).StartsWith("~$"))
            {
                Log.Debug($"Skipping `{filename}`: an Excel lock file, not a workbook.");
                continue;
            }

            string fileExtensions = Path.GetExtension(filename).ToLowerInvariant();
            if (!fileExtensionPatterns.Contains(fileExtensions))
                continue;

            // Relative to the directory being searched, so a recipe names a workbook the way
            // somebody looking at that directory would - `backup/Items.xlsx` rather than
            // whatever absolute path this run happened to be given.
            string workbook = Path.GetRelativePath(xlsx.Path, filename).Replace('\\', '/');

            _workbooksSeen.Add(workbook);

            // Asked before the file is opened. A workbook that is not input costs nothing to
            // decline here, and one of a real project is 80 MB of nothing this run wants.
            if (!_settings.Filter.IncludesWorkbook(workbook))
            {
                Log.Information($"Skipping workbook `{workbook}`: the recipe does not ask for it.");
                continue;
            }

            ImportXlsx(filename, workbook);
        }

        _settings.Filter.ReportUnmatchedIncludes(context.Section, _workbooksSeen, _sheetsSeen);

        // One line at the end as well as a warning each, because a hundred warnings in a
        // conversion's output is a thing nobody counts.
        if (_formulaErrorCount > 0)
        {
            Log.Warning(
                $"Recipe `{context.Section}` read {_formulaErrorCount} cell(s) holding a formula "
                + "error as empty, because it sets `OnFormulaError: \"empty\"`. Each one is logged "
                + "above with its cell.");
        }
    }

    private void ImportXlsx(string filename, string workbookName)
    {
        _currentWorkbook = workbookName;

        // Normalized once, here, so every Location built below shares this one instance.
        //
        // `Location.Filename` replaces `\` with `/` on the way in, and .NET's own directory
        // walk hands us paths that still have one - so that setter allocated a copy of the
        // path per cell. One workbook of six million cells was holding six million copies of
        // the same string: 1.6 GB, all of it the same hundred characters.
        string normalized = filename.Replace('\\', '/');

        ImportWorkbook(filename, normalized);
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

    /// <summary>
    /// The current workbook's defined names, or null when the layout does not use them.
    /// </summary>
    private List<WorkbookPackage.DefinedName>? _currentWorkbookNames;

    /// <param name="path">The file as the filesystem names it, for opening it.</param>
    /// <param name="filename">The same file with `/` separators, for every Location built here.</param>
    private void ImportWorkbook(string path, string filename)
    {
        // A layout that finds its tables by defined name is the only one that pays for
        // resolving them, because parsing a reference means resolving it and a workbook can
        // hold hundreds.
        bool usesNames = LayoutRegistry.UsesNamedRanges(_settings.Layout.Id);

        var package = WorkbookPackage.Read(path!, usesNames ? IsCandidateName : null);
        _currentWorkbookNames = usesNames ? package.DefinedNames : null;

        foreach (var skipped in package.SkippedNames)
        {
            // A name whose target was deleted, or one that is not a single rectangle. Worth
            // saying so rather than dropping in silence: in real workbooks these are
            // leftovers, and one of them being a table nobody exports any more is a thing
            // to know.
            Log.Warning(
                $"Defined name `{skipped.Name}` of `{filename}` refers to `{skipped.Reference}`, "
                + (skipped.Problem == WorkbookPackage.NameProblem.NotARange
                    ? "which is not a range. Skipped."
                    : "which this importer cannot read as a single rectangle. Skipped."));
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

            _sheetsSeen.Add((_currentWorkbook, sheetName));

            if (!_settings.Filter.Includes(_currentWorkbook, sheetName))
            {
                Log.Information($"Skipping sheet `{sheetName}` of `{filename}`: the recipe does not ask for it.");
                continue;
            }

            // A layout that finds its tables by defined name reads nothing from a sheet no
            // name covers, and says so and moves on.
            if (_currentWorkbookNames is not null && !CoveredByAName(sheetName))
            {
                Log.Information(
                    $"Skipping sheet `{sheetName}` of `{filename}`: no defined name covers it.");
                continue;
            }

            ImportSheet(reader, package, filename, sheetName);
        }
    }

    private void ImportSheet(
        SheetGridReader reader, WorkbookPackage package, string filename, string sheetName)
    {
        // Remembered so cell-level diagnostics can name where they came from; a cell knows
        // its row and column but not its workbook or sheet.
        _currentFilename = filename;
        _currentSheetName = sheetName;

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

                string value = reader.IsFormulaError(colIndex, out string excelText)
                    ? OnFormulaError(location, $"Cell contains the formula error `{excelText}`.")
                    : reader.Text(colIndex);

                rawRow.Add(new RawCell
                {
                    Location = location,
                    Value = value,
                    Note = hasNotes ? package.Note(sheetName, rowIndex, colIndex) : ""
                });
            }

            rawSheet.Rows.Add(rawRow);
        }

        if (!rawSheet.Optimize())
            return;


        AttachNamedRanges(rawSheet, sheetName);

        _model.Sheets.Add(rawSheet);
    }

    /// <summary>Whether any of the workbook's defined names points into this sheet.</summary>
    private bool CoveredByAName(string sheetName)
        => _currentWorkbookNames!.Any(
            name => string.Equals(name.SheetName, sheetName, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The workbook's defined names that point into this sheet, translated into the grid's
    /// coordinates.
    /// </summary>
    /// <remarks>
    /// Attached here because the importer is the only place that knows about names: by the
    /// time the cooker runs there is a cell grid and nothing to ask about names. Only for the
    /// layouts that use them - every other sheet gets an empty list and pays nothing.
    ///
    /// Translation rather than absolute coordinates, because <see cref="RawSheet.Optimize"/>
    /// has just trimmed the blank margins and everything downstream indexes the trimmed
    /// grid. The top-left cell knows where it came from, which is what the offset is.
    /// </remarks>
    private void AttachNamedRanges(RawSheet rawSheet, string sheetName)
    {
        if (_currentWorkbookNames is null || _currentWorkbookNames.Count == 0)
            return;

        // Where the trimmed grid sits in the sheet, so a name's cells can be found in it.
        var topLeft = rawSheet.Rows[0][0].Location;

        foreach (var named in _currentWorkbookNames)
        {
            if (!string.Equals(named.SheetName, sheetName, StringComparison.Ordinal))
                continue;

            // The filter applies to the name as well as to the sheet, because in a layout
            // that reads defined names the name is what a table is called - and a workbook
            // holds names that are not tables. A single-column range behind a data-validation
            // dropdown is the common one; the project this layout serves keeps a list of
            // exactly those in its own exporter's config, which is the same job.
            if (!_settings.Filter.Includes(_currentWorkbook, named.Name))
            {
                Log.Information(
                    $"Skipping defined name `{named.Name}` of `{_currentFilename}`: "
                    + "the recipe does not ask for it.");
                continue;
            }

            int row = named.FirstRow - topLeft.Row;
            int column = named.FirstColumn - topLeft.Column;

            // A name may cover rows or columns the grid no longer has - trailing blanks
            // are exactly what Optimize removes, and a range drawn generously over them is
            // ordinary. Clamped rather than refused, so the table is the cells that exist.
            int height = Math.Min(named.LastRow - named.FirstRow + 1, rawSheet.Rows.Count - row);
            int width = Math.Min(named.LastColumn - named.FirstColumn + 1, rawSheet.ColumnCount - column);

            if (row < 0 || column < 0 || height <= 0 || width <= 0)
            {
                Log.Warning(
                    $"Defined name `{named.Name}` of `{_currentFilename}` covers "
                    + $"{named.Reference}, which is outside the cells sheet `{sheetName}` has. Skipped.");
                continue;
            }

            rawSheet.NamedRanges.Add(new RawNamedRange
            {
                Name = named.Name,
                Row = row,
                Column = column,
                Height = height,
                Width = width,
            });
        }
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
    private string OnFormulaError(Location location, string what)
    {
        if (_settings.Layout.OnFormulaError == FormulaErrorPolicy.Error)
        {
            throw new TabbitException(location,
                $"{what} Fix the formula, or replace it with a literal value. "
                + "The source entry's `OnFormulaError: \"empty\"` reads cells like this as "
                + "empty instead, for a workbook this run does not own.");
        }

        _formulaErrorCount++;
        Log.Warning($"{what} Read as empty.\n    at {location}");

        return "";
    }

    /// <summary>How many formula errors this run swallowed, for the summary at the end.</summary>
    private int _formulaErrorCount;
}
