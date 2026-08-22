using Google.Apis.Auth.OAuth2;
using Google.Apis.Sheets.v4;
using Google.Apis.Services;
using Google.Apis.Util.Store;

using System;
using System.IO;
using System.Text;
using System.Threading;
using Tabbit.Models;
using Tabbit.Models.Raw;
using System.Collections.Generic;
using Tabbit.Cooking.Layouts;
using Tabbit.Extensions;
using Tabbit.Recipe;
using Serilog;
using System.Diagnostics;
using Tabbit.Sources;
using Tabbit.Messages;

namespace Tabbit.Importers;

[TabbitSource("googlesheets", "Sources.GoogleSheets", Order = 20)]
public class GoogleSheetsImporter
    : Source<RecipeModel.SourceRecipeGroup.GoogleSheetsRecipe>, ISourceVersions
{
    /// <summary>
    /// What Drive says this entry's document is at, for the build cache.
    /// </summary>
    /// <remarks>
    /// One request, before anything is imported. An entry that is switched off yields
    /// nothing, and a document whose version cannot be read yields a null version - which
    /// the cache reads as "fetch it and see".
    /// </remarks>
    IEnumerable<SourceVersion> ISourceVersions.Versions(SourceContext context)
    {
        var recipe = (RecipeModel.SourceRecipeGroup.GoogleSheetsRecipe)context.Entry;

        if (string.IsNullOrWhiteSpace(recipe.SheetsId) ||
            !GoogleSheetsCredentials.IsConfigured(recipe))
        {
            yield break;
        }

        yield return new SourceVersion(
            "googlesheets", recipe.SheetsId, GoogleSheetsVersion.Read(recipe, context.Section));
    }

    /// <summary>Which step of a run this class's log lines belong to.</summary>
    private static Serilog.ILogger Log => LogCategory.Importing;

    static string ApplicationName = "Tabbit";

    /// <summary>
    /// What the credential is asked to cover.
    /// </summary>
    /// <remarks>
    /// The second scope is metadata only, and it is what lets a run find out that a document
    /// has not changed without downloading it - the difference between a re-run that costs a
    /// fetch and one that costs a single request. It reads no cell of any file.
    ///
    /// Asking for it does not break a machine that has not granted it: the sheets themselves
    /// are read under the first scope, and the version request failing means the document is
    /// imported the way it always was. spec/build-cache.md §6.5.
    /// </remarks>
    static string[] Scopes = [SheetsService.Scope.SpreadsheetsReadonly, GoogleSheetsVersion.Scope];

    private RawModel _model = null!;

    private SheetImportSettings _settings = null!;

    protected override void Import(
        SourceContext context, RecipeModel.SourceRecipeGroup.GoogleSheetsRecipe googleSheets)
    {
        // An entry naming no document or no credential is switched off, matching how the
        // Excel source treats a blank path - which is how an entry is commented out in
        // practice: its contents are removed but the object stays in the list.
        //
        // This check was missing, and its absence was not harmless: a blank
        // ClientSecretFilename went straight into a FileStream, so a recipe with an
        // emptied-out Google Sheets entry failed with an argument exception about a path
        // rather than being skipped.
        if (string.IsNullOrWhiteSpace(googleSheets.SheetsId) ||
            !GoogleSheetsCredentials.IsConfigured(googleSheets))
        {
            Log.Debug($"Skipping Google Sheets source `{context.Section}`: not configured.");
            return;
        }

        _model = context.Model;
        _settings = SheetImportSettings.From(googleSheets, context.Section);

        var sheetsService = AcquireSheetsService(googleSheets, context.Section);
        // Recorded before the fetch, so a run that reads the document also records what
        // version it read - which is what the next run compares against. A version that
        // cannot be read is not recorded at all, and the next run then has nothing to
        // compare and fetches, which is the right answer.
        string? version = GoogleSheetsVersion.Read(googleSheets, context.Section);

        if (version is not null)
            context.Inputs.Remote("googlesheets", googleSheets.SheetsId, version);

        ImportSheets(sheetsService, googleSheets, context.Section);
    }

    private SheetsService AcquireSheetsService(
        RecipeModel.SourceRecipeGroup.GoogleSheetsRecipe recipe, string section)
    {
        var credential = GoogleSheetsCredentials.Acquire(recipe, section, Scopes, ApplicationName);

        // Create Google Sheets API service.
        return new SheetsService(new BaseClientService.Initializer()
        {
            HttpClientInitializer = credential,
            ApplicationName = ApplicationName,
        });
    }

    private void ImportSheets(
        SheetsService sheetsService, RecipeModel.SourceRecipeGroup.GoogleSheetsRecipe recipe, string section)
    {
        var sheetsId = recipe.SheetsId;
        var request = sheetsService.Spreadsheets.Get(sheetsId);
        request.IncludeGridData = true;

        Log.Information($"Importing google-spreadsheets `{sheetsId}`");

        // Timed because this is the one step that can take tens of seconds, and a run
        // that looks stuck is usually just waiting on the API.
        var stopWatch = new Stopwatch();
        stopWatch.Start();
        var response = request.Execute();
        stopWatch.Stop();
        Log.Information($"   => It took {stopWatch.ElapsedMilliseconds} milliseconds to fetch the Google Sheets data.");

        var sheetsTitle = response.Properties.Title;
        if (sheetsTitle.StartsWith("#") || sheetsTitle.StartsWith("//"))
        {
            Log.Warning(Message.Of(ImportMessages.LogSheetExcluded,
                ("Sheet", sheetsTitle)).In(MessageCatalog.Current));
            return;
        }

        // This source presents one workbook, so the workbook lists have one candidate to
        // decide about - matched by title, since a document id is not a thing anybody would
        // write a pattern for. A `[title]sheet` pattern reaches its sheets the same way it
        // reaches a file's.
        if (!_settings.Filter.IncludesWorkbook(sheetsTitle))
        {
            Log.Information($"Skipping document `{sheetsTitle}`: the recipe does not ask for it.");

            // Still reported, because the entry naming a workbook that is not this one is a
            // recipe that asks for something it will never get - and the alternative is a
            // run that reads nothing and says it succeeded.
            _settings.Filter.ReportUnmatchedIncludes(
                section, [sheetsTitle], []);

            return;
        }

        var sheetsSeen = new List<(string Workbook, string Sheet)>();

        // A layout that finds its tables by defined name is the only one that pays for
        // resolving them. Null for every other layout, and that null is also what says the
        // "no name covers this sheet" rule does not apply.
        var namesBySheet = LayoutRegistry.UsesNamedRanges(_settings.Layout.Id)
            ? ResolveNamedRanges(response, sheetsTitle)
            : null;

        foreach (var sheet in response.Sheets)
        {
            string sheetTitle = sheet.Properties.Title.Trim();

            if (sheetTitle.StartsWith("#") || sheetTitle.StartsWith("//"))
            {
                Log.Warning(Message.Of(ImportMessages.LogTabExcluded,
                    ("Sheet", sheetsTitle), ("Tab", sheetTitle)).In(MessageCatalog.Current));
                continue;
            }

            sheetsSeen.Add((sheetsTitle, sheetTitle));

            if (!_settings.Filter.Includes(sheetsTitle, sheetTitle))
            {
                Log.Information($"Skipping sheet `{sheetsTitle}.{sheetTitle}`: the recipe does not ask for it.");
                continue;
            }

            // A layout that finds its tables by defined name reads nothing from a sheet no
            // name covers, and says so and moves on. Unlike the workbook source this saves
            // nothing - the cells have already arrived with the response - but the two
            // sources decide the same thing about the same sheet.
            List<SheetNamedRange>? sheetNames = null;
            if (namesBySheet is not null &&
                !namesBySheet.TryGetValue(sheet.Properties.SheetId ?? 0, out sheetNames))
            {
                Log.Information(
                    $"Skipping sheet `{sheetsTitle}.{sheetTitle}`: no defined name covers it.");
                continue;
            }

            if (sheet.Data is null)
                continue;

            foreach (var d in sheet.Data)
            {
                if (d is null || d.RowData is null)
                    continue;

                int startColumn = d.StartColumn ?? 0;
                int startRow = d.StartRow ?? 0;

                RawSheet rawSheet = new RawSheet
                {
                    Layout = _settings.Layout,
                    Location = new Location
                    {
                        //Filename = spreadsheetsId,
                        //Sheet = sheetTitle,
                        Column = startColumn,
                        Row = startRow
                    }
                };

                rawSheet.Location.Filename = $"googlesheets.{sheetsTitle}";///{sheetTitle}",
                rawSheet.Location.Sheet = sheetTitle;
                rawSheet.Location.SheetUrl = MakeGoogleSheetsUrl(sheetsId, sheet.Properties.SheetId ?? 0);

                Log.Information($"Importing google-spreadsheets sheet `{rawSheet.Location.SheetUrl}`");

                int rowIndex = startRow;

                foreach (var r in d.RowData)
                {
                    if (r.Values is null)
                    {
                        rowIndex++;
                        continue;
                    }

                    List<RawCell> rawRow = [];

                    int colIndex = startColumn;
                    foreach (var v in r.Values)
                    {
                        string value = v.FormattedValue.SafeTrim();
                        string note = v.Note.SafeTrim();

                        RawCell rawCell = new RawCell
                        {
                            Location = new Location
                            {
                                Sheet = sheetTitle,
                                Column = colIndex,
                                Row = rowIndex
                            },
                            Value = value,
                            Note = note
                        };

                        rawCell.Location.Filename = $"googlesheets.{sheetsTitle}";///{sheetTitle}",
                        rawCell.Location.Sheet = sheetTitle;
                        rawCell.Location.SheetUrl = MakeGoogleSheetsUrl(sheetsId, sheet.Properties.SheetId ?? 0, rawCell.Location.CellRange);

                        rawRow.Add(rawCell);

                        colIndex++;
                    }

                    rawSheet.Rows.Add(rawRow);

                    rowIndex++;
                }

                if (!rawSheet.Optimize())
                    continue;

                // After Optimize, because it trims the blank margins and the translation is
                // against what is left. A name that points outside this block is reported
                // and dropped there, which is what happens when a sheet arrives as more
                // than one block of grid data.
                if (sheetNames is not null)
                {
                    SheetNamedRanges.Attach(
                        rawSheet, sheetNames, _settings.Filter, sheetsTitle,
                        rawSheet.Location.Filename);
                }

                _model.Sheets.Add(rawSheet);
            }
        }

        _settings.Filter.ReportUnmatchedIncludes(section, [sheetsTitle], sheetsSeen);
    }

    /// <summary>
    /// The document's defined names, grouped by the id of the sheet they point into.
    /// </summary>
    /// <remarks>
    /// Read out of the response the importer already has: `Spreadsheets.Get` returns the
    /// named ranges beside the grid data, so this costs no request of its own.
    ///
    /// Far less work than the same thing on a workbook, because a `GridRange` is one
    /// rectangle by construction - there is no union to reject and no reference text to
    /// parse. What is left to check is that all four sides are bounded: a name covering a
    /// whole column arrives with its row indexes absent, which means "as far as the sheet
    /// goes" rather than a rectangle of known extent.
    /// </remarks>
    internal static Dictionary<int, List<SheetNamedRange>> ResolveNamedRanges(
        Google.Apis.Sheets.v4.Data.Spreadsheet response, string documentTitle)
    {
        var bySheet = new Dictionary<int, List<SheetNamedRange>>();

        if (response.NamedRanges is null)
            return bySheet;

        foreach (var named in response.NamedRanges)
        {
            string name = (named.Name ?? "").Trim();
            if (name.Length == 0)
                continue;

            var range = named.Range;

            // Worth saying so rather than dropping in silence: in real documents these are
            // leftovers, and one of them being a table nobody exports any more is a thing
            // to know.
            if (range is null || range.SheetId is null)
            {
                Log.Warning(Message.Of(ImportMessages.LogDefinedNameNoRange,
                    ("Name", name), ("Document", documentTitle)).In(MessageCatalog.Current));
                continue;
            }

            if (range.StartRowIndex is not int firstRow || range.EndRowIndex is not int endRow ||
                range.StartColumnIndex is not int firstColumn ||
                range.EndColumnIndex is not int endColumn)
            {
                Log.Warning(Message.Of(ImportMessages.LogDefinedNameNotOneRectangle,
                    ("Name", name), ("Document", documentTitle),
                    ("Range", Describe(range))).In(MessageCatalog.Current));
                continue;
            }

            // The API's end indexes are exclusive. Every coordinate past this point is
            // inclusive, as a workbook's own reference is.
            var resolved = new SheetNamedRange(
                Name: name,
                Reference: Describe(range),
                FirstRow: firstRow,
                FirstColumn: firstColumn,
                LastRow: endRow - 1,
                LastColumn: endColumn - 1);

            if (!bySheet.TryGetValue(range.SheetId.Value, out var names))
                bySheet[range.SheetId.Value] = names = [];

            names.Add(resolved);
        }

        return bySheet;
    }

    /// <summary>
    /// A grid range in A1 notation, for a diagnostic to name what a defined name covers.
    /// </summary>
    /// <remarks>
    /// The API carries a rectangle as numbers and never as text, so the text has to be built
    /// here - and it is written the way a workbook's reference is written, so the same
    /// message reads the same whichever source produced it. An absent index is left out,
    /// which is how a whole column comes out as `A:A` and a whole row as `1:1`.
    /// </remarks>
    private static string Describe(Google.Apis.Sheets.v4.Data.GridRange range)
    {
        string first = Corner(range.StartRowIndex, range.StartColumnIndex);
        string last = Corner(
            range.EndRowIndex is int row ? row - 1 : null,
            range.EndColumnIndex is int column ? column - 1 : null);

        return first.Length == 0 && last.Length == 0 ? "the whole sheet" : $"{first}:{last}";
    }

    /// <summary>One corner in A1 notation, with an absent index left out.</summary>
    private static string Corner(int? row, int? column)
        => (column is int c ? ColumnName(c) : "") + (row is int r ? (r + 1).ToString() : "");

    /// <summary>A zero-based column index as its letters: 0 is `A`, 26 is `AA`.</summary>
    private static string ColumnName(int column)
    {
        var letters = new StringBuilder();

        // Bijective base 26: there is no zero digit, so each place is 1..26 and the step
        // down subtracts one before dividing.
        for (int n = column + 1; n > 0; n = (n - 1) / 26)
            letters.Insert(0, (char)('A' + (n - 1) % 26));

        return letters.ToString();
    }

    //https://webapps.stackexchange.com/questions/44473/link-to-a-cell-in-a-google-sheets-via-url
    private string MakeGoogleSheetsUrl(string sheetsId, int sheetId, string? cellRange = null)
    {
        if (!string.IsNullOrEmpty(cellRange))
            return $"https://docs.google.com/spreadsheets/d/{sheetsId}/edit#gid={sheetId}&range={cellRange}";
        else
            return $"https://docs.google.com/spreadsheets/d/{sheetsId}/edit#gid={sheetId}";
    }
}
