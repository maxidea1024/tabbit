using System;
using Tabbit.Cooking;
using Tabbit.Helpers;
using Tabbit.Recipe;
using Tabbit.Sources;
using Xunit;

using ValueType = Tabbit.Models.ValueType;

namespace Tabbit.Tests;

/// <summary>
/// Which time zone a sheet's wall clock was written in, and the UTC value that comes of it.
/// </summary>
/// <remarks>
/// **What is being pinned here is that data leaves in UTC and that a recipe saying nothing
/// changes no value.** Those two together are the whole design: the second is what lets the
/// setting be added to a tool that has already shipped values, and without a test it is one
/// careless default away from moving every date a project has.
///
/// The cell winning over the setting is the other half. A cell that wrote `Z` named a moment,
/// and a run that read it again as somebody's local time would be wrong in a way nothing
/// downstream could detect - which is what used to happen, by the time zone of whichever
/// machine ran the conversion.
///
/// spec/types/datetime-timezone.md.
/// </remarks>
public class DateTimeTimeZoneTests
{
    private static CookingContext Context(string timeZone = "")
        => new CookingContext(
            new Tabbit.Models.Model(),
            new RecipeModel { TimeZone = timeZone },
            new Diagnostics());

    private static Tabbit.Models.Location Where()
        => new Tabbit.Models.Location { Filename = "memory.xlsx", Sheet = "T", Column = 1, Row = 1 };

    /// <summary>What a cell becomes, read under a recipe-wide zone.</summary>
    private static DateTime Read(string cell, string timeZone = "")
        => (DateTime)Context(timeZone).ParseValue(ValueType.DateTime, null, cell, Where())!;

    private static DateTime At(int year, int month, int day, int hour, int minute, int second = 0)
        => new DateTime(year, month, day, hour, minute, second);

    // ------------------------------------------------- a recipe that says nothing

    /// <summary>
    /// No setting reads a cell as already being in UTC, which is what every value read
    /// before this setting existed was.
    /// </summary>
    [Fact]
    public void Without_a_zone_a_wall_clock_is_taken_as_it_was_written()
        => Assert.Equal(At(2022, 1, 24, 10, 30), Read("2022-01-24 10:30:00"));

    /// <summary>
    /// A cell that wrote `Z` is the moment it named, whatever the machine's own zone is.
    /// </summary>
    /// <remarks>
    /// The regression this file exists for. Parsed with DateTimeStyles.None, `Z` was
    /// converted into the local zone of whoever ran the conversion - so a workbook produced
    /// one value on a designer's PC in Seoul and another on a build agent in UTC, with
    /// nothing in either run saying so.
    /// </remarks>
    [Fact]
    public void A_cell_that_wrote_utc_does_not_move_with_the_machine()
        => Assert.Equal(At(2022, 1, 24, 10, 30), Read("2022-01-24T10:30:00Z"));

    /// <summary>An offset in the cell is applied by the cell's own offset.</summary>
    [Fact]
    public void A_cell_that_wrote_an_offset_is_converted_by_it()
        => Assert.Equal(At(2022, 1, 24, 1, 30), Read("2022-01-24T10:30:00+09:00"));

    // --------------------------------------------------------- a zone by name

    [Fact]
    public void A_named_zone_reads_the_wall_clock_as_that_region_s()
        => Assert.Equal(At(2022, 1, 24, 1, 30), Read("2022-01-24 10:30:00", "Asia/Seoul"));

    /// <summary>
    /// Windows ids work where IANA ones do. Which family a recipe writes is its own business,
    /// and .NET reads both on both platforms.
    /// </summary>
    [Fact]
    public void A_windows_id_names_the_same_zone()
        => Assert.Equal(
            Read("2022-01-24 10:30:00", "Asia/Seoul"),
            Read("2022-01-24 10:30:00", "Korea Standard Time"));

    /// <summary>The region's history is what a name is for: a summer date converts by the
    /// offset that was in force that summer, not by the zone's standard one.</summary>
    [Fact]
    public void A_named_zone_uses_the_offset_that_was_in_force()
    {
        // New York is -05:00 in January and -04:00 in July.
        Assert.Equal(At(2022, 1, 24, 15, 30), Read("2022-01-24 10:30:00", "America/New_York"));
        Assert.Equal(At(2022, 7, 24, 14, 30), Read("2022-07-24 10:30:00", "America/New_York"));
    }

    // ------------------------------------------------------ a zone by offset

    /// <summary>
    /// The spellings of a fixed offset. All of them, because a recipe written by hand uses
    /// whichever one the author has seen, and an offset refused for its punctuation would
    /// send them looking for a zone name they do not have.
    /// </summary>
    [Theory]
    [InlineData("+09:00", 1, 30)]
    [InlineData("+0900", 1, 30)]
    [InlineData("+09", 1, 30)]
    [InlineData("+9", 1, 30)]
    [InlineData("-05:30", 16, 0)]
    [InlineData("-0530", 16, 0)]
    [InlineData("+05:45", 4, 45)]
    [InlineData("Z", 10, 30)]
    [InlineData("+00:00", 10, 30)]
    public void A_fixed_offset_is_written_the_way_offsets_are(string zone, int hour, int minute)
        => Assert.Equal(At(2022, 1, 24, hour, minute), Read("2022-01-24 10:30:00", zone));

    /// <summary>
    /// A fixed offset is the same all year, which is the difference between it and a name -
    /// and the reason sheets kept to one office's clock should use it.
    /// </summary>
    [Fact]
    public void A_fixed_offset_does_not_follow_daylight_saving()
    {
        Assert.Equal(At(2022, 1, 24, 15, 30), Read("2022-01-24 10:30:00", "-05:00"));
        Assert.Equal(At(2022, 7, 24, 15, 30), Read("2022-07-24 10:30:00", "-05:00"));
    }

    // ------------------------------------------------------- daylight saving

    /// <summary>
    /// A wall clock the region skipped is refused, naming the cell. Both readings of it are
    /// an hour from what the author meant, and neither is this tool's to choose.
    /// </summary>
    [Fact]
    public void A_time_the_clocks_jumped_over_is_refused()
    {
        var refusal = Assert.Throws<TabbitException>(
            () => Read("2022-03-13 02:30:00", "America/New_York"));

        Assert.Equal(Tabbit.Cooking.CookingMessages.TimeInDstGap, refusal.MessageId);
        Assert.Contains("America/New_York", refusal.Message);

        // The way out, for sheets whose authors are not going to rewrite the cell.
    }

    /// <summary>
    /// A wall clock the region passed through twice is read as the standard-time one and the
    /// run continues. Unlike the gap above there is a value to read, and one hour a year is
    /// not worth refusing a conversion over.
    /// </summary>
    [Fact]
    public void A_time_that_happened_twice_is_read_as_the_standard_one()
        => Assert.Equal(
            At(2022, 11, 6, 6, 30), Read("2022-11-06 01:30:00", "America/New_York"));

    // ---------------------------------------------------------------- arrays

    /// <summary>Every element of a `datetime[]` cell, not only the first.</summary>
    [Fact]
    public void The_elements_of_a_dated_array_are_converted()
    {
        var values = (DateTime[])Context("Asia/Seoul").ParseValue(
            ValueType.DateTimeArray, null, "2022-01-24 10:30:00;2022-01-25 10:30:00", Where())!;

        Assert.Equal(new[] { At(2022, 1, 24, 1, 30), At(2022, 1, 25, 1, 30) }, values);
    }

    // ----------------------------------------------------- entry over recipe

    /// <summary>
    /// A source entry's zone wins for its own sheets. Two teams' workbooks read in one run
    /// were filled in at two desks, and one of them is not the other's clock.
    /// </summary>
    [Fact]
    public void A_source_entry_s_zone_wins_over_the_recipe_s()
    {
        var context = Context("Asia/Seoul");
        var entry = TimeZones.OfEntry("-05:00", "Sources.Xlsx[0]");

        var value = (DateTime)context.ParseValue(
            ValueType.DateTime, null, "2022-01-24 10:30:00", Where(), timeZone: entry)!;

        Assert.Equal(At(2022, 1, 24, 15, 30), value);
    }

    /// <summary>
    /// An entry that says nothing takes the recipe's, which is what a recipe-wide setting is
    /// for.
    /// </summary>
    [Fact]
    public void An_entry_that_says_nothing_takes_the_recipe_s()
    {
        Assert.Null(TimeZones.OfEntry("", "Sources.Xlsx[0]"));

        var settings = SheetImportSettings.From(
            new RecipeModel.SourceRecipeGroup.XlsxRecipe { TimeZone = "" }, "Sources.Xlsx[0]");

        Assert.Null(settings.Layout.TimeZone);
    }

    /// <summary>An entry's setting is resolved when the entry is read, not per cell.</summary>
    [Fact]
    public void An_entry_s_zone_is_resolved_before_any_sheet_is_read()
    {
        var settings = SheetImportSettings.From(
            new RecipeModel.SourceRecipeGroup.XlsxRecipe { TimeZone = "+09:00" }, "Sources.Xlsx[0]");

        Assert.Equal(TimeSpan.FromHours(9), settings.Layout.TimeZone!.BaseUtcOffset);
    }

    // --------------------------------------------------- what a setting refuses

    /// <summary>
    /// A name no zone answers to is refused with the names that nearly match. The spelling
    /// that gets written is the city, and it is nowhere near an edit distance from the id.
    /// </summary>
    [Fact]
    public void An_unknown_name_is_answered_with_the_names_that_hold_it()
    {
        var refusal = Assert.Throws<TabbitException>(() => TimeZones.OfRecipe("Seoul"));

        Assert.Equal(Tabbit.Recipe.RecipeMessages.TimeZoneUnknown, refusal.MessageId);
        Assert.Contains("TimeZone", refusal.Message);
    }

    /// <summary>
    /// A recipe entry's refusal names the entry. A run reads several, and "not a time zone"
    /// without a section is a search through the whole recipe.
    /// </summary>
    [Fact]
    public void An_entry_s_refusal_names_the_entry()
    {
        var refusal = Assert.Throws<TabbitException>(
            () => SheetImportSettings.From(
                new RecipeModel.SourceRecipeGroup.XlsxRecipe { TimeZone = "Mars/Olympus" }, "Sources.Xlsx[0]"));

        Assert.Equal(Tabbit.Recipe.RecipeMessages.TimeZoneUnknown, refusal.MessageId);
        Assert.Contains("Sources.Xlsx[0]", refusal.Message);
    }

    /// <summary>
    /// Something written as an offset and malformed is answered as an offset rather than as
    /// a misspelled zone name. `+9:5` is not a place anybody meant.
    /// </summary>
    [Theory]
    [InlineData("+9:5")]
    [InlineData("+09:00:00")]
    [InlineData("+")]
    [InlineData("-1x:00")]
    [InlineData("+09:70")]
    public void A_malformed_offset_is_refused_as_an_offset(string written)
    {
        var refusal = Assert.Throws<TabbitException>(() => TimeZones.OfRecipe(written));

        Assert.Equal(Tabbit.Recipe.RecipeMessages.TimeZoneNotAnOffset, refusal.MessageId);
    }

    [Theory]
    [InlineData("+15:00")]
    [InlineData("-14:30")]
    public void An_offset_no_place_on_earth_keeps_is_refused(string written)
    {
        var refusal = Assert.Throws<TabbitException>(() => TimeZones.OfRecipe(written));

        Assert.Equal(Tabbit.Recipe.RecipeMessages.TimeZoneOffsetTooLarge, refusal.MessageId);
    }

    // ------------------------------------------------ forced from the command line

    /// <summary>
    /// `--time-zone` wins over the recipe, which is what a forced option is for: a run whose
    /// recipe is wrong about the zone cannot be fixed by a setting the recipe also holds.
    /// </summary>
    [Fact]
    public void The_option_wins_over_the_recipe()
    {
        var recipe = new RecipeModel { TimeZone = "Asia/Seoul" };

        CommandLineTimeZone.Apply(new Options { TimeZone = "-05:00" }, recipe);

        Assert.Equal("-05:00", recipe.TimeZone);
    }

    /// <summary>
    /// And over each source entry's, which is the half that would otherwise be missed: an
    /// entry's zone beats the recipe-wide one by design, so an option that only set the
    /// recipe would lose to the lines it exists to override.
    /// </summary>
    [Fact]
    public void The_option_wins_over_every_source_entry()
    {
        var recipe = new RecipeModel { TimeZone = "Asia/Seoul" };
        recipe.Sources.Xlsx.Add(
            new RecipeModel.SourceRecipeGroup.XlsxRecipe { TimeZone = "America/New_York" });
        recipe.Sources.GoogleSheets.Add(
            new RecipeModel.SourceRecipeGroup.GoogleSheetsRecipe { TimeZone = "+05:45" });

        CommandLineTimeZone.Apply(new Options { TimeZone = "-05:00" }, recipe);

        Assert.Equal("-05:00", recipe.TimeZone);
        Assert.All(recipe.Sources.SheetEntries(), entry => Assert.Equal("", entry.TimeZone));
    }

    /// <summary>A run without the option is the run there always was.</summary>
    [Fact]
    public void Without_the_option_the_recipe_is_left_alone()
    {
        var recipe = new RecipeModel { TimeZone = "Asia/Seoul" };
        recipe.Sources.Xlsx.Add(
            new RecipeModel.SourceRecipeGroup.XlsxRecipe { TimeZone = "America/New_York" });

        CommandLineTimeZone.Apply(new Options(), recipe);

        Assert.Equal("Asia/Seoul", recipe.TimeZone);
        Assert.Equal("America/New_York", recipe.Sources.Xlsx[0].TimeZone);
    }

    /// <summary>
    /// A zone the option names badly stops the run before a workbook is opened, and the
    /// message names the option rather than the recipe - there is nothing in the recipe to
    /// go and fix.
    /// </summary>
    [Fact]
    public void A_malformed_option_names_the_option()
    {
        var refusal = Assert.Throws<TabbitException>(
            () => CommandLineTimeZone.Apply(
                new Options { TimeZone = "Mars/Olympus" }, new RecipeModel()));

        Assert.Equal(Tabbit.Recipe.RecipeMessages.TimeZoneUnknown, refusal.MessageId);
        Assert.Contains("--time-zone", refusal.Message);
    }

    // ------------------------------------------------------- what is stored

    /// <summary>
    /// The value carries no Kind. What is stored is a number of ticks that the exports read
    /// as UTC by contract - and a value marked Utc is refused by the PostgreSQL writer for a
    /// `timestamp` column, so marking it would make this setting change the shape of exports
    /// as well as their values.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("Asia/Seoul")]
    [InlineData("+09:00")]
    public void The_stored_value_carries_no_kind(string zone)
    {
        Assert.Equal(DateTimeKind.Unspecified, Read("2022-01-24 10:30:00", zone).Kind);
        Assert.Equal(DateTimeKind.Unspecified, Read("2022-01-24T10:30:00Z", zone).Kind);
    }

    /// <summary>
    /// A duration is not a moment, so no zone applies to it. Worth a test because both types
    /// are ticks on the wire and the parse for them sits on adjacent lines.
    /// </summary>
    [Fact]
    public void A_timespan_is_not_touched_by_a_zone()
        => Assert.Equal(
            new TimeSpan(1, 2, 3, 4),
            (TimeSpan)Context("Asia/Seoul").ParseValue(
                ValueType.TimeSpan, null, "1.02:03:04", Where())!);
}
