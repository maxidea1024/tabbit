using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Tabbit.Reporting;

/// <summary>When a run opens its own report, and how.</summary>
/// <remarks>
/// The opening is the feature. A report written to a path and mentioned in a log line is a
/// log line: the person who can fix the sheet is not watching the console, and the moment a
/// problem appears is the moment they can be reached. spec/ops/build-report.md §7.
///
/// The deciding is separated from the opening so that a test can ask the question without a
/// browser appearing on whoever is running it. What the gate checks is the decision.
/// </remarks>
public static class ReportOpening
{
    /// <summary>What a recipe may ask for.</summary>
    public enum Policy
    {
        /// <summary>Never open it. The path is still printed.</summary>
        Never,

        /// <summary>Open it when there is something to fix.</summary>
        Problems,

        /// <summary>Open it on every run.</summary>
        Always,
    }

    /// <summary>Why a run did not open a report it would otherwise have opened.</summary>
    public enum Suppression
    {
        /// <summary>Nothing suppressed it.</summary>
        None,

        /// <summary>The `CI` environment variable is set: nobody is at this screen.</summary>
        ContinuousIntegration,

        /// <summary>Output is going somewhere other than a terminal, so a script ran this.</summary>
        NotATerminal,

        /// <summary>`--silent`. Being quiet was already asked for.</summary>
        Silent,
    }

    /// <summary>
    /// Reads the recipe's setting, refusing a spelling nobody meant.
    /// </summary>
    /// <remarks>
    /// Refused rather than defaulted. A recipe that says `problmes` means to open the report
    /// and would silently never open one, which is a setting that looks set and is not.
    /// </remarks>
    public static Policy PolicyOf(string written)
        => (written ?? "").Trim().ToLowerInvariant() switch
        {
            "" or "problems" => Policy.Problems,
            "never" => Policy.Never,
            "always" => Policy.Always,
            _ => throw new TabbitException(null, Messages.Message.Of(
                Recipe.RecipeMessages.ReportOpenUnknown, ("Written", written))),
        };

    /// <summary>
    /// What, if anything, stops this run opening a browser whatever the recipe says.
    /// </summary>
    /// <remarks>
    /// All three are the same signal in different clothes: there is no person in front of
    /// this run. They beat the setting rather than being weighed against it, because a
    /// build agent that opens a browser opens it for nobody and leaves a process behind.
    /// </remarks>
    public static Suppression SuppressedBy(bool silent, bool outputRedirected, string? ciVariable)
    {
        if (!string.IsNullOrEmpty(ciVariable))
            return Suppression.ContinuousIntegration;

        if (outputRedirected)
            return Suppression.NotATerminal;

        if (silent)
            return Suppression.Silent;

        return Suppression.None;
    }

    /// <summary>Whether the report is worth putting in front of somebody.</summary>
    public static bool Wanted(Policy policy, bool hasProblems)
        => policy switch
        {
            Policy.Always => true,
            Policy.Problems => hasProblems,
            _ => false,
        };

    /// <summary>How this run reads its surroundings when nothing overrides them.</summary>
    public static Suppression SuppressedHere(bool silent)
        => SuppressedBy(silent, Console.IsOutputRedirected, Environment.GetEnvironmentVariable("CI"));

    /// <summary>
    /// Hands a file to whatever opens one on this machine.
    /// </summary>
    /// <remarks>
    /// Replaced by the tests, which have to answer "would it have opened" without a browser
    /// appearing. Nothing else assigns it.
    ///
    /// A failure here is a failure to be convenient. It is reported and then dropped: a run
    /// whose exit code depended on whether a browser could be found would fail on machines
    /// that have none, having done all of its work correctly.
    /// </remarks>
    public static Func<string, bool> Opener { get; set; } = LaunchTheDefaultBrowser;

    private static bool LaunchTheDefaultBrowser(string path)
    {
        try
        {
            string target = System.IO.Path.GetFullPath(path);

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // Through the shell, which is what knows the file association. Starting the
                // path directly needs an executable.
                Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
                return true;
            }

            string opener = RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "open" : "xdg-open";

            Process.Start(new ProcessStartInfo(opener, target) { UseShellExecute = false });
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
