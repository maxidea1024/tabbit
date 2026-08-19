using System.Collections.Generic;
using Newtonsoft.Json;

namespace Tabbit.Recipe;

/// <summary>
/// What every sheet-reading source takes, whatever it reads the sheets from.
/// </summary>
/// <remarks>
/// A workbook on disk and a hosted spreadsheet document differ in how they are fetched and
/// in nothing else: both arrive as a grid of cells, and both need the same three answers -
/// which workbooks and sheets to take, how to read what is in them, and what to do with data
/// that a stricter project would not have written. Those answers live here so a recipe says
/// the same thing whichever source it names.
/// </remarks>
public abstract class SheetSourceRecipe
{
    /// <summary>
    /// Which layout parser reads these sheets.
    /// </summary>
    /// <remarks>
    /// `tabbit` is the layout this tool defines: entities are declared with
    /// `~~table:Name~~` markers and can sit anywhere on a sheet. It is the default.
    ///
    /// Others exist for reading sheets written to a convention this tool did not choose,
    /// without rewriting them first. Each registers itself, so the list of what is available
    /// comes from the build rather than from here - an unknown id is answered with the ids
    /// that do exist.
    /// </remarks>
    public string Layout { get; set; } = "tabbit";

    /// <summary>
    /// Separator for array cells in these sheets. Blank takes the recipe-wide setting.
    /// </summary>
    /// <remarks>
    /// The delimiter is a property of how a set of sheets was written, so it belongs beside
    /// the entry that reads them: two sets read in one run were authored under different
    /// conventions, and one of them using `|` should not force the other to. Set here, it
    /// wins over the recipe-wide `ArrayDelimiter` for this entry only.
    /// </remarks>
    public string ArrayDelimiter { get; set; } = "";

    /// <summary>
    /// Workbooks to read. An empty list means every workbook the source presents.
    /// </summary>
    /// <remarks>
    /// Written either as an array of names or as one semicolon-separated string, with `*` and
    /// `?` matching the way they do in a file glob.
    ///
    /// A pattern is matched against the workbook's path relative to the directory searched,
    /// against its file name, and against that name without its extension - so `Items`,
    /// `Items.xlsx` and `shared/Items.xlsx` all name one workbook, while `backup/*` names a
    /// directory of them. A source presenting a single document matches it by title.
    ///
    /// A named workbook that turns out not to be there is an error, for the reason a named
    /// sheet is: the alternative is a table missing from the output with nothing in the run
    /// saying so.
    /// </remarks>
    [JsonConverter(typeof(StringListConverter))]
    public List<string> IncludeWorkbooks { get; set; } = new List<string>();

    /// <summary>
    /// Workbooks to skip, in the same form as <see cref="IncludeWorkbooks"/>. Applied after
    /// it.
    /// </summary>
    /// <remarks>
    /// The list that gets written in practice, because a directory pointed at a team's
    /// workbooks holds files that are not input: a copy kept for reference, a backup, a
    /// workbook whose contents were never tabular. Naming those is shorter than naming every
    /// workbook that is input, and it does not have to be revisited each time one is added.
    /// </remarks>
    [JsonConverter(typeof(StringListConverter))]
    public List<string> ExcludeWorkbooks { get; set; } = new List<string>();

    /// <summary>
    /// Sheets to read. An empty list means every sheet.
    /// </summary>
    /// <remarks>
    /// Written either as an array of names or as one semicolon-separated string, whichever
    /// suits its length. `*` and `?` match the way they do in a file glob, so `Char*` takes
    /// every sheet whose name starts with `Char`.
    ///
    /// A pattern may name the workbook it applies to, as `[Items.xlsx]Define` - because sheet
    /// names repeat across workbooks, and `Define` being a table in one of them and a scratch
    /// tab in another is a distinction a sheet name alone cannot make. The workbook part is a
    /// glob of its own, matched as <see cref="IncludeWorkbooks"/> matches; without brackets a
    /// pattern applies to every workbook, which is what it has always meant.
    ///
    /// Naming the sheets is worth the typing when a workbook holds more than the data:
    /// reference tabs, working notes and half-built tables all look like input to a reader
    /// that takes whatever it finds. A named sheet that turns out not to exist is an error
    /// rather than a silent omission, which is the point of naming them.
    /// </remarks>
    [JsonConverter(typeof(StringListConverter))]
    public List<string> IncludeSheets { get; set; } = new List<string>();

    /// <summary>
    /// Sheets to skip, in the same form as <see cref="IncludeSheets"/>. Applied after it.
    /// </summary>
    [JsonConverter(typeof(StringListConverter))]
    public List<string> ExcludeSheets { get; set; } = new List<string>();

    /// <summary>
    /// What to do when two rows carry the same index value: `error`, `keep-first` or
    /// `keep-last`.
    /// </summary>
    /// <remarks>
    /// `error` is the default and the only one that keeps the guarantee an index is for.
    /// The other two are for sheets whose source cannot be corrected right away: refusing to
    /// convert anything until every duplicate is fixed blocks the rest of the data for a
    /// reason that is not the reader's to fix. Both log every row they drop, so the choice is
    /// visible in the run rather than only here.
    ///
    /// Whether a layout honours it is the layout's decision. Sheets written in the `tabbit`
    /// layout are always checked, because there the duplicate can be fixed where it is; a
    /// layout reading sheets the converting team does not own is where the concession earns
    /// its place.
    /// </remarks>
    public string OnDuplicateIndex { get; set; } = "error";

    /// <summary>
    /// What to do about a cell whose formula evaluated to an error: `error` or `empty`.
    /// </summary>
    /// <remarks>
    /// `error` is the default, and it is the right answer for sheets the team converting can
    /// fix: a `#REF!` reaching the game as a value is exactly what this tool exists to stop.
    ///
    /// `empty` is for workbooks somebody else maintains. One broken formula in a column
    /// nothing reads otherwise refuses the conversion of every table in the workbook, which
    /// answers a question nobody asked. Every cell it swallows is warned about, so the count
    /// is visible in the run and can be handed back to whoever owns the sheet.
    /// </remarks>
    public string OnFormulaError { get; set; } = "error";

    /// <summary>
    /// What to do about a blank cell where the column's type has no reading for one:
    /// `error` or `empty`.
    /// </summary>
    /// <remarks>
    /// `error` is the default and the strict one. A blank in a number, date, uuid or enum
    /// column is usually a row somebody stopped filling in, and reading it as zero puts a
    /// value in the data that cannot be told from a zero somebody typed - the human error
    /// this tool exists to catch, passing through it.
    ///
    /// `empty` reads such a cell as the type's empty value and warns once per column, for
    /// the case `OnFormulaError: "empty"` exists for: sheets another team maintains, where
    /// one unfinished cell would otherwise refuse every table in the workbook.
    ///
    /// **Neither setting decides what absence is.** A row that has no value writes `-`, and
    /// that is only allowed where the column's type ends in `?`. This setting answers a
    /// different question - whether a cell nobody filled in stops the run - and a blank read
    /// as empty is a cell with a value, presence bit and all. spec/blank-and-null-cells.md.
    /// </remarks>
    public string OnBlankCell { get; set; } = "error";

    /// <summary>
    /// How a table name says it is another set of some table's rows, as a regular expression
    /// with a `table` group and a `set` group. Blank, which is the default, means no table
    /// here has more than one set.
    /// </summary>
    /// <remarks>
    /// Some sheets fill a table's columns in more than once - the same schema with a second
    /// set of rows, so that a build can be made with one or the other - and say so by naming
    /// the extra sets after the table. What the tail looks like is the sheets' own convention,
    /// so it is written here rather than known anywhere in this program:
    ///
    ///     "TableRowSets": "^(?&lt;table&gt;.+?)(?&lt;set&gt;_BC[A-Z]+)$"
    ///
    /// reads `Admiral_BCCN` as another set of `Admiral`'s rows, named `_BCCN`. The `set` group
    /// is captured rather than composed, so the separator is whatever the sheets wrote and
    /// the file comes out spelled the way the rest of that project spells it.
    ///
    /// A canonical property rather than a layout option, because "does this project's table
    /// have more than one set of rows" is a question about the sheets and not about how a
    /// table is found in them - a marker layout and a defined-name layout can both have it.
    ///
    /// **What it is not**: a way to make two tables. A matched name contributes rows to the
    /// table it points at, and its schema has to be that table's. It produces no type of its
    /// own, and a name matching this whose table is absent is an error rather than a new
    /// table - see spec/table-row-sets.md.
    /// </remarks>
    public string TableRowSets { get; set; } = "";

    /// <summary>
    /// Whether consecutively numbered columns fold into one array-valued field.
    /// </summary>
    /// <remarks>
    /// Off unless asked for, because whether a number in a column name means an array is a
    /// question about intent that a name cannot answer. `Text1` and `Text2` usually do mean
    /// one array of two. `Condition_1`, `Condition_2` and `Condition_3` of one real workbook
    /// are three different enums, and folding them is not a nicer API but a wrong one.
    ///
    /// Being wrong here is quiet, which is the reason for the default. A folded group takes a
    /// name the sheet never used - `Text_array` - and three fields become one, so a consumer
    /// reads an array where the author wrote three separate things.
    ///
    /// Only a layout that has the convention reads this. A layout for sheets written to
    /// somebody else's rules has no numbering convention to honour, so the setting does not
    /// apply to it at all rather than defaulting to false there.
    /// </remarks>
    public bool FoldSerialFields { get; set; }

    /// <summary>
    /// Whether an array drops the elements at its end that a row left empty, so its length is
    /// what that row filled in rather than the number of columns.
    /// </summary>
    /// <remarks>
    /// Off unless asked for. A fixed-length array pads the rows that did not fill it, and the
    /// padding is indistinguishable from values: `{ Id = 0, Count = 0 }` could be a slot
    /// giving nothing or no slot at all. Trimming says which - and it says it in the data
    /// rather than in a convention each consumer has to reimplement.
    ///
    /// Off is the default because turning it on shortens arrays, and shorter is quiet: a
    /// consumer indexing `Slot[2]` finds it on some rows and not others. Which cells count as
    /// empty is the layout's answer, because that is a question about how the sheet is
    /// written.
    ///
    /// Record arrays and scalar ones alike: trimming answers "where do the elements end",
    /// and what an element looks like has no bearing on that question.
    ///
    /// Only the end is dropped. An empty element between two filled ones stays, so element
    /// `k` is always the column numbered `k`.
    /// </remarks>
    public bool TrimTrailingArrayElements { get; set; }

    /// <summary>
    /// Whether an array may have an empty element between two filled ones.
    /// </summary>
    /// <remarks>
    /// Off, so a gap stops the conversion and names the cell. A gap is almost always a
    /// mistake - a row whose `Slot2` was cleared and whose `Slot3` was left alone - and
    /// today it becomes an array whose middle element holds the type's empty value, where
    /// a consumer cannot tell "absent" from "zero".
    ///
    /// Turning it on keeps the middle exactly as
    /// spec/variable-length-record-arrays.md describes it. The default is the strict one
    /// because a lenient default turns a mistake into data, and a strict default costs the
    /// one line that says the gap was meant.
    ///
    /// What counts as the middle is decided after trimming: with trimming on, the blanks
    /// past the last value are outside the array and are not gaps.
    /// </remarks>
    public bool AllowArrayGaps { get; set; }

    /// <summary>
    /// Settings that belong to the layout this entry names, as free-form key/value pairs.
    /// </summary>
    /// <remarks>
    /// The core does not know the keys and does not validate them. A layout reads the ones it
    /// recognizes through <see cref="Models.Raw.SheetLayout.Option"/>, and is expected to
    /// report one it does not - so a typo is still answered, by the code that would have used
    /// it.
    ///
    /// Here because a layout for sheets this tool did not design will want knobs, and putting
    /// each one in this class by name would spell that project into the core recipe. The day
    /// such a layout is deleted its settings would still be in every recipe's schema and the
    /// documentation would still explain them. A bag costs one property and takes that whole
    /// class of change out of the core.
    ///
    /// Not a substitute for a real setting. Anything that applies whatever the layout - the
    /// array delimiter, the duplicate-index policy, formula errors - is a property above,
    /// because a reader should find those without knowing which layout is in play.
    /// </remarks>
    public Dictionary<string, string> LayoutOptions { get; set; } = new Dictionary<string, string>();
}
