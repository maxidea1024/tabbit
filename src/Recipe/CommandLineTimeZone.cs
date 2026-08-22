using System;
using System.Linq;
using Serilog;
using Tabbit.Helpers;

namespace Tabbit.Recipe;

/// <summary>
/// Forces the time zone the sheets are read in, from `--time-zone`.
/// </summary>
/// <remarks>
/// Written onto the recipe rather than carried alongside it. The zone is read in two places -
/// the recipe-wide setting and each source entry's - and a third answer threaded past both
/// would leave every reader of either having to know which of the three won. Stamping it here
/// means one thing decides, once, and everything downstream reads what it always read.
///
/// This is why it clears the entries as well as setting the recipe: an entry's own zone beats
/// the recipe-wide one by design, so leaving them would make an option named "force" lose to
/// the very lines it is there to override.
/// </remarks>
public static class CommandLineTimeZone
{
    /// <summary>Which step of a run this class's log lines belong to.</summary>
    private static Serilog.ILogger Log => LogCategory.Loading;

    /// <summary>
    /// Applies the option, or does nothing when it was not given.
    /// </summary>
    /// <remarks>
    /// Called before anything is imported, so a zone that names no place stops the run with
    /// no work done rather than after every workbook has been read.
    /// </remarks>
    public static void Apply(Options? options, RecipeModel recipe)
    {
        string forced = (options?.TimeZone ?? "").Trim();

        if (forced.Length == 0)
            return;

        // Read for its refusal. The resolved zone is discarded, because what is stored is
        // the text: the recipe is what the run's readers consult, and two representations
        // of one setting is how they come to disagree.
        TimeZones.OfCommandLine(forced);

        var overridden = recipe.Sources.SheetEntries()
            .Where(entry => (entry.TimeZone ?? "").Trim().Length > 0)
            .Select(entry => entry.TimeZone.Trim())
            .Distinct()
            .ToList();

        recipe.TimeZone = forced;

        foreach (var entry in recipe.Sources.SheetEntries())
            entry.TimeZone = "";

        // Said out loud, and said once. An option that moves every date in the output is
        // worth a line in the log of the run that used it - reconstructing it afterwards
        // means finding the command line, which is the one thing the output does not hold.
        Log.Information($"Reading every sheet's dates as `{forced}`, from --time-zone.");

        if (overridden.Count > 0)
        {
            Log.Information(
                "This overrides the source entries that set their own: "
                + string.Join(", ", overridden.Select(zone => $"`{zone}`")) + ".");
        }
    }
}
