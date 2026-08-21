using System;
using MySqlConnector;
using Serilog;
using Tabbit.Exporters;
using Tabbit.Targets;

namespace Tabbit.History;

/// <summary>
/// Settings for the history target.
/// </summary>
public sealed class HistoryRecipe : IOutputRecipe
{
    /// <summary>
    /// Where the history lives.
    ///
    /// Supports `${NAME}` placeholders filled from the environment, and a password
    /// belongs in one of those rather than in a recipe that gets committed.
    /// </summary>
    public string ConnectionString { get; set; } = "";

    /// <summary>
    /// Which project's history this is.
    ///
    /// One database can hold several. Changing this starts a new history rather than
    /// continuing the old one, so it is worth getting right the first time.
    /// </summary>
    public string ProjectKey { get; set; } = "";

    /// <summary>
    /// Whether to record a conversion whose working copy had uncommitted changes.
    ///
    /// Off by default. Such a conversion holds work no commit describes, so filing it
    /// under the last commit credits it to whoever made that commit - and once it is in,
    /// the next clean build of that same commit cannot be recorded, because a snapshot
    /// for it already exists describing different data.
    /// </summary>
    public bool RecordDirty { get; set; } = false;

    /// <summary>
    /// Whether to record a commit older than the one the branch already ends at.
    ///
    /// Off by default. Snapshots form a chain and each one's changes are measured
    /// against the one before, so recording an older commit after a newer one reports
    /// the newer commit's work as having been undone - by the author of the older one.
    /// </summary>
    public bool AllowOutOfOrder { get; set; } = false;

    /// <summary>
    /// What to do when the history cannot be reached: `warn` or `fail`.
    ///
    /// `warn` by default. A build produces game data, and a database being down is not
    /// a reason to stop producing it. The cost is a gap: the next snapshot's changes are
    /// measured from the last one recorded, so they span the commits in between and are
    /// attributed to the commit that finally got through. The gap is visible - two
    /// consecutive snapshots whose commits are not adjacent - rather than silent.
    /// </summary>
    public string OnFailure { get; set; } = "warn";

    /// <summary>Which side this entry is built for. The history itself is never narrowed.</summary>
    public string TargetSide { get; set; } = "cs";
}

/// <summary>
/// Records what this conversion changed, so that who changed what, and when, has an
/// answer.
///
/// Thin on purpose: the decisions live in <see cref="HistoryRecorder"/>, where they can
/// be exercised against a real database without a conversion around them. What is left
/// here is the recipe, the connection, and the policy for a database that is down.
/// </summary>
[TabbitTarget("history", TargetKind.Description, Order = 20)]
public class HistoryTarget : Target<HistoryRecipe>
{
    /// <summary>Which step of a run this class's log lines belong to.</summary>
    private static Serilog.ILogger Log => LogCategory.Recording;

    protected override void Run(TargetContext context, HistoryRecipe recipe)
    {
        // An entry left in the recipe with a blank connection string is switched off,
        // as a blank path is for every file target.
        if (string.IsNullOrWhiteSpace(recipe.ConnectionString))
            return;

        if (string.IsNullOrWhiteSpace(recipe.ProjectKey!))
        {
            throw new TabbitException(
                $"Recipe `{context.Section}` records history but names no ProjectKey. One database " +
                $"can hold several projects and they are told apart by it.");
        }

        if (!HistoryRecorder.CanRecord(context.Commit, recipe))
            return;

        // The unnarrowed model, always. A history taken from a client build would record
        // every server-only table as deleted, and the next server build would record
        // them all as added again.
        var summary = SummaryBuilder.Build(context.FullModel, context.Commit, context);
        var fingerprint = ModelFingerprint.Of(context.FullModel);

        string connectionString = ConnectionString.Resolve(recipe.ConnectionString, context.Section);

        try
        {
            Log.Debug($"Connecting to the history at `{ConnectionString.Redact(connectionString)}`");

            using var store = HistoryStore.Open(connectionString!, recipe.ProjectKey!, context.Commit.Branch!);

            HistoryRecorder.Record(store, summary, fingerprint, context.Commit, recipe, out _);
        }
        catch (Exception ex) when (IsReachabilityProblem(ex))
        {
            Unreachable(recipe, context.Section, ex);
        }
    }

    /// <summary>
    /// The history is unreachable. Whether that stops the build is the recipe's call.
    /// </summary>
    private static void Unreachable(HistoryRecipe recipe, string? section, Exception ex)
    {
        if (!string.Equals(recipe.OnFailure, "warn", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(recipe.OnFailure, "fail", StringComparison.OrdinalIgnoreCase))
        {
            throw new TabbitException(
                $"Recipe `{section}` sets OnFailure to `{recipe.OnFailure}`, which is not a value. " +
                $"Use `warn` or `fail`.");
        }

        string message =
            $"The history at `{section}` could not be reached, so nothing was recorded for this " +
            $"conversion: {ex.Message}";

        if (string.Equals(recipe.OnFailure, "fail", StringComparison.OrdinalIgnoreCase))
            throw new TabbitException(message);

        // Error rather than warning. The build is not failing, but a gap has opened: the
        // next snapshot's changes will be measured from the last one recorded and
        // attributed to whichever commit finally gets through.
        Log.Error(message);
        Log.Error("The next recorded snapshot will cover this commit's changes as well as its own.");
    }

    /// <summary>
    /// Whether the failure is the database being unreachable rather than the data being
    /// wrong.
    ///
    /// A connection that cannot be made is an operational problem the recipe has a
    /// policy for. A duplicate key or a constraint violation is a defect here, and
    /// swallowing it under the same policy would let the history quietly stop recording.
    /// </summary>
    private static bool IsReachabilityProblem(Exception ex)
    {
        if (ex is MySqlException mysql)
        {
            switch (mysql.ErrorCode)
            {
                case MySqlErrorCode.UnableToConnectToHost:
                case MySqlErrorCode.ConnectionCountError:
                case MySqlErrorCode.AccessDenied:
                case MySqlErrorCode.UnknownDatabase:
                    return true;
            }

            // A timeout arrives as a MySqlException wrapping a socket or timeout error
            // rather than with a server error code of its own.
            return mysql.InnerException is TimeoutException
                   || mysql.InnerException is System.Net.Sockets.SocketException;
        }

        return ex is TimeoutException || ex is System.Net.Sockets.SocketException;
    }
}
