using Tabbit.Messages;

namespace Tabbit.Validation;

/// <summary>
/// The reports validation writes about the rules themselves, named.
/// </summary>
/// <remarks>
/// `validate` because that is the step of the run these come from - the same names
/// <see cref="LogCategory"/> uses.
///
/// **Not what the rules report.** A rule writes its own findings through the context it is
/// handed, in whatever words its author chose; those are the recipe's, not this tool's, and
/// nothing here can name them. What is here is everything this pipeline says about a rule
/// file - that it has no entry, that it named a table this model does not have, that it threw.
///
/// Several of these carry a `{Detail}` that came from somewhere else - the compiler's own
/// message, or a caught exception's. The frame is translatable and what it quotes arrives as
/// it was written.
/// </remarks>
[TabbitMessages("validate")]
public static class ValidationMessages
{
    /// <summary>A rule file with no entry method to run.</summary>
    public const string RuleHasNoEntry = "validate.rule-has-no-entry";

    /// <summary>A rule file declaring its entry method more than once.</summary>
    public const string RuleEntryDeclaredTwice = "validate.rule-entry-declared-twice";

    /// <summary>An entry method that does not take the context its folder hands over.</summary>
    public const string RuleEntryWrongContext = "validate.rule-entry-wrong-context";

    /// <summary>A compiler error in a rule file.</summary>
    public const string RuleCompileError = "validate.rule-compile-error";

    /// <summary>A compiler warning in a rule file.</summary>
    public const string RuleCompileWarning = "validate.rule-compile-warning";

    /// <summary>A rule refused by the pipeline while it ran - a setting nobody configured.</summary>
    public const string RuleRefused = "validate.rule-refused";

    /// <summary>A rule that threw something the pipeline did not expect.</summary>
    public const string RuleThrew = "validate.rule-threw";

    /// <summary>A rule that reported more than the run will show.</summary>
    public const string ReportsOverCap = "validate.reports-over-cap";

    /// <summary>Runtime rules left unrun because the run asked for that.</summary>
    public const string RuntimeRulesSkipped = "validate.runtime-rules-skipped";

    /// <summary>Rules left unrun because an earlier tier reported.</summary>
    public const string RulesSkippedAfterTier = "validate.rules-skipped-after-tier";

    /// <summary>A table rule whose file name does not say which table it is for.</summary>
    public const string TableRuleUnnamed = "validate.table-rule-unnamed";

    /// <summary>A table rule for a table this model does not have.</summary>
    public const string TableRuleOrphaned = "validate.table-rule-orphaned";

    /// <summary>A table rule declaring a tier, which table rules have no use for.</summary>
    public const string TableRuleDeclaresTier = "validate.table-rule-declares-tier";

    /// <summary>A tier that is not a plain number.</summary>
    public const string TierUnreadable = "validate.tier-unreadable";

    /// <summary>A validation folder with no rule files under it.</summary>
    public const string NoRuleFiles = "validate.no-rule-files";

    /// <summary>A pre-stage rule reading tables, which do not exist yet.</summary>
    public const string TablesBeforeSheetsRead = "validate.tables-before-sheets-read";

    /// <summary>A pre-stage rule reading the schema, which does not exist yet.</summary>
    public const string SchemaBeforeSheetsRead = "validate.schema-before-sheets-read";

    /// <summary>A rule reading a validation option the recipe does not set.</summary>
    public const string OptionNotSet = "validate.option-not-set";

    /// <summary>A rule outside the runtime stage opening an external store.</summary>
    public const string StoreOutsideRuntimeStage = "validate.store-outside-runtime-stage";

    /// <summary>`Files()` called with no folder.</summary>
    public const string FilesNeedsFolder = "validate.files-needs-folder";

    /// <summary>`Files()` pointed at a folder that is not there.</summary>
    public const string FilesFolderMissing = "validate.files-folder-missing";

    /// <summary>`Json()` pointed at a file that is not there.</summary>
    public const string JsonFileMissing = "validate.json-file-missing";

    /// <summary>A file `Json()` opened that is not JSON.</summary>
    public const string JsonUnreadable = "validate.json-unreadable";

    /// <summary>A `Validation.Path` resolving to a folder that is not there.</summary>
    public const string PathFolderMissing = "validate.path-folder-missing";

    /// <summary>A subfolder of the rules folder that is not a stage.</summary>
    public const string UnknownSubfolder = "validate.unknown-subfolder";

    /// <summary>One stage folder left where the stages used to live.</summary>
    public const string StageAtRoot = "validate.stage-at-root";

    /// <summary>As <see cref="StageAtRoot"/>, for more than one.</summary>
    public const string StagesAtRoot = "validate.stages-at-root";

    /// <summary>`--new-validator` with no `Validation.Path` to write into.</summary>
    public const string NoPathForNewRule = "validate.no-path-for-new-rule";

    /// <summary>`--new-validator` with no table named.</summary>
    public const string NewValidatorNeedsTable = "validate.new-validator-needs-table";

    /// <summary>`--new-validator` pointed at a rule file that already exists.</summary>
    public const string RuleFileExists = "validate.rule-file-exists";

    /// <summary>A database a rule queried that answered with a failure.</summary>
    public const string StoreQueryFailed = "validate.store-query-failed";

    /// <summary>A cache a rule read that answered with a failure.</summary>
    public const string StoreReadFailed = "validate.store-read-failed";

    /// <summary>Something other than a read passed to a store.</summary>
    public const string StoreNotAQuery = "validate.store-not-a-query";

    /// <summary>`Db()` given a connection that is not a database.</summary>
    public const string ConnectionNotADatabase = "validate.connection-not-a-database";

    /// <summary>`Redis()` given a connection that is not Redis.</summary>
    public const string ConnectionNotRedis = "validate.connection-not-redis";

    /// <summary>A connection a rule opened that the recipe does not have.</summary>
    public const string ConnectionNotConfigured = "validate.connection-not-configured";

    /// <summary>A connection string with no scheme in front of it.</summary>
    public const string ConnectionNoScheme = "validate.connection-no-scheme";

    /// <summary>The headline over the reports that stopped a validation stage.</summary>
    public const string StageFailed = "validate.stage-failed";
}
