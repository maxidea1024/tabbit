using Tabbit.Messages;

namespace Tabbit.History;

/// <summary>
/// The reports recording a run, and answering questions about earlier ones, produces.
/// </summary>
/// <remarks>
/// `record` because that is the step - <see cref="LogCategory.Recording"/> covers recording
/// what a run produced and answering questions about earlier ones.
///
/// The history is the one feature with commands of its own, so most of these are about what
/// was typed rather than about a sheet: a `--before` that is neither a date nor an age, a
/// `--bind` that would serve the history to the network with no token set. They are here
/// rather than under a prefix of their own because the person reading them is doing one
/// thing - asking the history something - and splitting the report by whether the mistake was
/// in the recipe or on the command line would not help them.
///
/// What is not here: `Assets`-style recipe settings the history target reads, which are
/// <see cref="Recipe.RecipeMessages"/> like every other setting, and the value renderer's own
/// switch default, which is a <see cref="TabbitDefectException"/>.
/// </remarks>
[TabbitMessages("record")]
public static class RecordMessages
{
    /// <summary>A `--commit-date` that is not a timestamp.</summary>
    public const string CommitDateNotADate = "record.commit-date-not-a-date";

    /// <summary>`--prune` with nothing saying how far back to go.</summary>
    public const string PruneNeedsBound = "record.prune-needs-bound";

    /// <summary>A `--before` that is neither a date nor an age.</summary>
    public const string BeforeNotADateOrAge = "record.before-not-a-date-or-age";

    /// <summary>A `--format` that is not one of the three.</summary>
    public const string FormatUnknown = "record.format-unknown";

    /// <summary>A recipe with no history target to read.</summary>
    public const string NoHistoryTarget = "record.no-history-target";

    /// <summary>A recipe with several history targets and no `--project`.</summary>
    public const string SeveralHistoryTargets = "record.several-history-targets";

    /// <summary>A `--project` no history target in the recipe matches.</summary>
    public const string NoTargetForProject = "record.no-target-for-project";

    /// <summary>A project the history database does not hold.</summary>
    public const string ProjectUnknown = "record.project-unknown";

    /// <summary>A metric name that is not one of the ones there are.</summary>
    public const string MetricUnknown = "record.metric-unknown";

    /// <summary>As <see cref="MetricUnknown"/>, where per-table narrows the list.</summary>
    public const string MetricUnknownPerTable = "record.metric-unknown-per-table";

    /// <summary>A commit prefix that matches more than one commit.</summary>
    public const string CommitAmbiguous = "record.commit-ambiguous";

    /// <summary>A range whose ends are the wrong way round.</summary>
    public const string RangeReversed = "record.range-reversed";

    /// <summary>A commit whose recorded model is not the one being recorded now.</summary>
    public const string ModelDiffersForCommit = "record.model-differs-for-commit";

    /// <summary>A pooled value that vanished between being written and being read.</summary>
    public const string ValuePoolIdMissing = "record.value-pool-id-missing";

    /// <summary>A history the run could not reach, where the recipe says to fail.</summary>
    public const string Unreachable = "record.unreachable";

    /// <summary>Another process holding the lock this one needs.</summary>
    public const string LockHeld = "record.lock-held";

    /// <summary>Another process part-way through a schema migration.</summary>
    public const string MigrationLockHeld = "record.migration-lock-held";

    /// <summary>A history database newer than the build reading it.</summary>
    public const string SchemaNewerThanBuild = "record.schema-newer-than-build";

    /// <summary>A schema migration that failed part-way.</summary>
    public const string MigrationFailed = "record.migration-failed";

    /// <summary>A `--bind` that is not an address.</summary>
    public const string BindNotAnAddress = "record.bind-not-an-address";

    /// <summary>A `--bind` open to the network with no token set.</summary>
    public const string BindPublicWithoutToken = "record.bind-public-without-token";

    /// <summary>A query-string value that had to be a number.</summary>
    public const string QueryValueNotANumber = "record.query-value-not-a-number";

    /// <summary>A commit the history holds nothing for, and no checkout to resolve it in.</summary>
    public const string SnapshotNotFound = "record.snapshot-not-found";

    /// <summary>A log line: Branch `{Branch}` of `{Project}` has no snapshots.</summary>
    public const string LogBranchHasNoSnapshots = "record.log-branch-has-no-snapshots";

    /// <summary>A log line: Commit {Commit} is behind {Head}, which the history already holds for branch `{Branch}`.</summary>
    public const string LogCommitIsBehind = "record.log-commit-is-behind";

    /// <summary>A log line: The next recorded snapshot will cover this commit's changes as well as its own.</summary>
    public const string LogNextSnapshotCoversThis = "record.log-next-snapshot-covers-this";

    /// <summary>A log line: This conversion has no commit identity, so its changes cannot be attributed to anyone.</summary>
    public const string LogNoCommitIdentity = "record.log-no-commit-identity";

    /// <summary>A log line: The history has no snapshot for `{At}` on branch `{Branch}`.</summary>
    public const string LogNoSnapshotAt = "record.log-no-snapshot-at";

    /// <summary>A log line: Nothing was recorded in the history for this conversion.</summary>
    public const string LogNothingRecorded = "record.log-nothing-recorded";

    /// <summary>A log line: Nothing was recorded in the history for this conversion.</summary>
    public const string LogNothingRecordedDirty = "record.log-nothing-recorded-dirty";

    /// <summary>A log line: The history holds nothing for project `{Project}`.</summary>
    public const string LogProjectEmpty = "record.log-project-empty";

    /// <summary>A log line: The history holds nothing for project `{Project}`.</summary>
    public const string LogProjectEmptyRunFirst = "record.log-project-empty-run-first";

    /// <summary>A log line: The working copy at `{Path}` has uncommitted changes, so this conversion does not match commit {.</summary>
    public const string LogWorkingCopyDirty = "record.log-working-copy-dirty";
}
