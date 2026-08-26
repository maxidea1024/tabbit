using System;
using System.Collections.Generic;
using System.Linq;

namespace Tabbit.Models.Raw;

/// <summary>
/// What to do about two rows carrying the same index value.
/// </summary>
public enum DuplicateIndexPolicy
{
    /// <summary>Report it, which is what an index is for.</summary>
    Error,

    /// <summary>Keep the row that appeared first and drop the rest, logging each.</summary>
    KeepFirst,

    /// <summary>Keep the row that appeared last and drop the rest, logging each.</summary>
    KeepLast,
}

/// <summary>
/// What to do about a cell whose formula evaluated to an error.
/// </summary>
public enum FormulaErrorPolicy
{
    /// <summary>Report it and stop, which is what a broken formula deserves.</summary>
    Error,

    /// <summary>
    /// Read the cell as empty and warn, so one broken formula does not hide everything
    /// else a conversion would have said.
    /// </summary>
    Empty,
}

/// <summary>
/// What to do about a blank cell in a column whose type has no reading for one.
/// </summary>
/// <remarks>
/// A `string`, a `bool` and an array read a blank as a value they already have - the empty
/// string, false, no elements - and this does not apply to them. It is about the types where
/// a blank is nothing at all: the numbers, the dates, a uuid, an enum label.
///
/// Saying a row has no value is a different statement, written `-`, and neither setting here
/// changes that. spec/types/blank-and-null-cells.md.
/// </remarks>
public enum BlankCellPolicy
{
    /// <summary>Report it and name the cell, which is what a blank where a number belongs is for.</summary>
    Error,

    /// <summary>
    /// Read it as the type's empty value and warn once per column, for workbooks another
    /// team maintains and this one cannot correct.
    /// </summary>
    Empty,
}

/// <summary>
/// How a sheet is to be read, carried from the recipe entry that imported it.
/// </summary>
/// <remarks>
/// A tag rather than a lookup: sources append to one shared raw model, so by the time the
/// cooker sees a sheet there is nothing left to say which entry brought it in. Two entries
/// in different layouts therefore work in one run, which is the case that matters: two sets
/// of sheets written to different conventions are read side by side into one model.
/// </remarks>
public sealed class SheetLayout
{
    /// <summary>The layout every sheet gets when a recipe entry does not name one.</summary>
    public static readonly SheetLayout Default = new SheetLayout("tabbit", DuplicateIndexPolicy.Error);

    public SheetLayout(
        string id, DuplicateIndexPolicy onDuplicateIndex, char? arrayDelimiter = null,
        FormulaErrorPolicy onFormulaError = FormulaErrorPolicy.Error,
        IReadOnlyDictionary<string, string>? options = null,
        bool trimTrailingArrayElements = false,
        bool allowArrayGaps = false,
        string tableRowSets = "",
        BlankCellPolicy onBlankCell = BlankCellPolicy.Error,
        TimeZoneInfo? timeZone = null)
    {
        Id = id;
        OnDuplicateIndex = onDuplicateIndex;
        ArrayDelimiter = arrayDelimiter;
        TimeZone = timeZone;
        OnFormulaError = onFormulaError;
        OnBlankCell = onBlankCell;
        TrimTrailingArrayElements = trimTrailingArrayElements;
        AllowArrayGaps = allowArrayGaps;
        TableRowSets = tableRowSets;
        _options = options ?? new Dictionary<string, string>();
    }

    /// <summary>
    /// The pattern that recognizes a table name as another set of some table's rows, or
    /// blank when this source has no such names.
    /// </summary>
    /// <remarks>
    /// Carried as the recipe wrote it. It is applied after every layout has parsed, because
    /// the names it pairs up can be read in either order, so nothing here interprets it -
    /// see <see cref="Tabbit.Cooking.TableRowSets"/>.
    /// </remarks>
    public string TableRowSets { get; }

    /// <summary>
    /// Whether a record array drops the elements at its end that a row left empty.
    /// </summary>
    /// <remarks>
    /// Off unless a recipe entry asks, because turning it on makes arrays shorter and shorter
    /// is quiet - a consumer finds `Slot[2]` present on some rows and missing on others.
    ///
    /// A layout whose sheets always trim ignores this and trims regardless; the rule belongs
    /// where the sheets it describes are.
    /// </remarks>
    public bool TrimTrailingArrayElements { get; }

    /// <summary>
    /// Whether an array may have an empty element between two filled ones.
    /// </summary>
    /// <remarks>
    /// Off, so a gap stops the conversion and names the cell. spec/types/variable-length-record-arrays.md
    /// says why the strict reading is the default one.
    /// </remarks>
    public bool AllowArrayGaps { get; }


    private readonly IReadOnlyDictionary<string, string> _options;

    /// <summary>
    /// A setting that belongs to this layout rather than to every layout, or null when the
    /// entry did not set one.
    /// </summary>
    /// <remarks>
    /// The core carries these without knowing what they mean, so a layout for sheets this
    /// tool did not design can have its own knobs without its name appearing in the recipe
    /// model - and without leaving settings behind in every recipe's schema the day it goes.
    ///
    /// A layout is expected to answer a key it does not recognize, because nothing else can:
    /// a typo here is invisible to the core by construction. <see cref="RequireKnownOptions"/>
    /// is the usual way to do that.
    /// </remarks>
    public string? Option(string key)
    {
        if (_options is null)
            return null;

        return _options.TryGetValue(key, out string? value) ? value : null;
    }

    /// <summary>
    /// Reports any option this layout does not recognize, naming the ones it does.
    /// </summary>
    /// <remarks>
    /// Called by a layout that reads options, once, with every key it knows. Without it a
    /// misspelled key is silently ignored - the failure this tool exists to prevent, in its
    /// own configuration.
    /// </remarks>
    public void RequireKnownOptions(string section, params string[] known)
    {
        if (_options is null || _options.Count == 0)
            return;

        foreach (var key in _options.Keys)
        {
            if (known.Contains(key, StringComparer.OrdinalIgnoreCase))
                continue;

                throw new TabbitException(null,
                    Messages.Message.Of(Recipe.RecipeMessages.LayoutOptionUnknown,
                        ("Section", section), ("Key", key), ("Layout", Id),
                        ("Known", known.Length == 0 ? "none" : string.Join(", ", known))));
        }
    }

    /// <summary>Id of the layout parser that reads this sheet.</summary>
    public string Id { get; }

    /// <summary>
    /// What a repeated index value does to the run.
    /// </summary>
    /// <remarks>
    /// A layout decides whether to honour it. Anything but `Error` is a concession to sheets
    /// whose source cannot be corrected right away, so a layout whose sheets the converting
    /// team owns has no reason to offer one.
    /// </remarks>
    public DuplicateIndexPolicy OnDuplicateIndex { get; }

    /// <summary>
    /// What a cell holding a formula error does to the run.
    /// </summary>
    /// <remarks>
    /// Read by the importers rather than a layout parser, because the value never reaches
    /// one: a `#REF!` is refused while the cells are being read.
    ///
    /// Set per source entry for the same reason the duplicate-index policy is - it is a
    /// statement about one set of sheets, not about the run. A workbook somebody else
    /// maintains can have a broken formula in a column nothing reads, and refusing to
    /// convert the other six hundred tables over it answers a question nobody asked.
    /// </remarks>
    public FormulaErrorPolicy OnFormulaError { get; }

    /// <summary>
    /// What a blank cell does where the column's type has no reading for one.
    /// </summary>
    /// <remarks>
    /// Per source entry for the reason the two policies above are: it says whether these
    /// sheets are ours to correct. A workbook this team owns should stop on a blank where a
    /// number belongs, and one another team maintains cannot be stopped on for the same
    /// reason its broken formula cannot.
    ///
    /// It does not decide what absence is. A row saying it has no value writes `-`, in every
    /// layout and whatever this holds.
    /// </remarks>
    public BlankCellPolicy OnBlankCell { get; }

    /// <summary>
    /// Separator for array cells in these sheets, or null to use the recipe-wide one.
    /// </summary>
    /// <remarks>
    /// Per entry because the delimiter is a property of how a sheet was written, not of the
    /// run: two sets of sheets read together were authored by different people under
    /// different conventions, and one of them writing `1|2|3` should not force the other to.
    /// </remarks>
    public char? ArrayDelimiter { get; }

    /// <summary>
    /// The time zone these sheets' wall clocks were written in, or null to use the
    /// recipe-wide setting.
    /// </summary>
    /// <remarks>
    /// Resolved when the entry was read rather than carried as the text of a name, so a
    /// setting that names no zone is reported before a sheet is opened, and so the lookup
    /// happens once an entry instead of once a cell.
    /// </remarks>
    public TimeZoneInfo? TimeZone { get; }

    public override string ToString() => Id;
}
