using Tabbit.Messages;

namespace Tabbit.Exporters;

/// <summary>
/// The reports writing a run's output produces.
/// </summary>
/// <remarks>
/// `export` because that is the step - <see cref="LogCategory.Exporting"/> covers writing
/// every export and generating every language's code, and these are its refusals.
///
/// What is here is what a recipe or an environment got wrong: a connection string with no
/// database in it, a key file that is not there, a `text` target given two answers to what a
/// file looks like. What is deliberately not here is the exporters' own invariants - a MAC key
/// of the wrong length, a wire element the binary writer has no case for. Those are
/// <see cref="TabbitDefectException"/> and stay in English, because the person who can act on
/// them is us. spec/message-ids.md §3.
/// </remarks>
[TabbitMessages("export")]
public static class ExportMessages
{
    /// <summary>A database section with no connection string.</summary>
    public const string ConnectionStringMissing = "export.connection-string-missing";

    /// <summary>A connection string naming environment variables that are not set.</summary>
    public const string ConnectionStringEnvNotSet = "export.connection-string-env-not-set";

    /// <summary>A MongoDB connection string that names no database.</summary>
    public const string MongoDbNeedsDatabase = "export.mongodb-needs-database";

    /// <summary>An element type the MongoDB exporter has no mapping for.</summary>
    public const string MongoDbElementTypeUnmapped = "export.mongodb-element-type-unmapped";

    /// <summary>A column type the MySQL exporter has no mapping for.</summary>
    public const string MySqlTypeUnmapped = "export.mysql-type-unmapped";

    /// <summary>A column type the PostgreSQL exporter has no mapping for.</summary>
    public const string PostgreSqlTypeUnmapped = "export.postgresql-type-unmapped";

    /// <summary>Redis refusing the transaction that swaps a shadow key into place.</summary>
    public const string RedisSwapRefused = "export.redis-swap-refused";

    /// <summary>A schema change an already-built reader could not survive.</summary>
    public const string SchemaBrokeReaders = "export.schema-broke-readers";

    /// <summary>A schema baseline file that could not be read.</summary>
    public const string SchemaBaselineUnreadable = "export.schema-baseline-unreadable";

    /// <summary>A file offered for decryption that is not encrypted.</summary>
    public const string FileNotEncrypted = "export.file-not-encrypted";

    /// <summary>A cipher byte this build has no implementation for.</summary>
    public const string CipherUnknown = "export.cipher-unknown";

    /// <summary>A file that did not decrypt, which means the wrong key.</summary>
    public const string DecryptFailed = "export.decrypt-failed";

    /// <summary>One key given for both encryption and the MAC.</summary>
    public const string SameKeyForMac = "export.same-key-for-mac";

    /// <summary>A key named both in the environment and in a file.</summary>
    public const string KeyNamedTwice = "export.key-named-twice";

    /// <summary>A key expected in an environment variable that is not set.</summary>
    public const string KeyEnvNotSet = "export.key-env-not-set";

    /// <summary>A key expected in a file that does not exist.</summary>
    public const string KeyFileMissing = "export.key-file-missing";

    /// <summary>A key of the wrong length.</summary>
    public const string KeyWrongLength = "export.key-wrong-length";

    /// <summary>A key that is not hexadecimal.</summary>
    public const string KeyNotHexadecimal = "export.key-not-hexadecimal";

    /// <summary>The `text` target given both a format and a template.</summary>
    public const string TextFormatAndTemplate = "export.text-format-and-template";

    /// <summary>The `text` target given neither.</summary>
    public const string TextNeedsFormat = "export.text-needs-format";

    /// <summary>Columns gathering into one group that name different namespaces.</summary>
    public const string TextGroupTwoNamespaces = "export.text-group-two-namespaces";

    /// <summary>A pattern that opens a brace and never closes it.</summary>
    public const string TextPatternUnclosedBrace = "export.text-pattern-unclosed-brace";

    /// <summary>A pattern naming something this target does not fill in.</summary>
    public const string TextPatternUnknownName = "export.text-pattern-unknown-name";

    /// <summary>A per-file name used in a pattern written once per string.</summary>
    public const string TextPatternNameIsPerFile = "export.text-pattern-name-is-per-file";

    /// <summary>A per-string name used in a header or footer.</summary>
    public const string TextPatternNameIsPerString = "export.text-pattern-name-is-per-string";

    /// <summary>A template file that is not there.</summary>
    public const string TextTemplateMissing = "export.text-template-missing";

    /// <summary>A line ending that is neither `lf` nor `crlf`.</summary>
    public const string TextLineEndingUnknown = "export.text-line-ending-unknown";
    /// <summary>A constant whose type a generator has no rendering for.</summary>
    public const string ConstantTypeNotRendered = "export.constant-type-not-rendered";

    /// <summary>A target that does not carry optional fields yet.</summary>
    public const string TargetNoOptionalFields = "export.target-no-optional-fields";

    /// <summary>A target handed a table keyed by several columns taken together.</summary>
    public const string TargetNoCompositeKeys = "export.target-no-composite-keys";

    /// <summary>A target that does not carry optional array elements yet.</summary>
    public const string TargetNoOptionalElements = "export.target-no-optional-elements";

    /// <summary>A target that does not carry record groups yet.</summary>
    public const string TargetNoNestedFields = "export.target-no-nested-fields";

    /// <summary>A target that does not carry a record inside a record yet.</summary>
    public const string TargetNoRecordInRecord = "export.target-no-record-in-record";

    /// <summary>Two model entities whose names reduce to one file name.</summary>
    public const string GeneratedFileNameClash = "export.generated-file-name-clash";

    /// <summary>A log line: Could not clean up MongoDB shadow collections: {Detail}.</summary>
    public const string LogMongodbCleanupFailed = "export.log-mongodb-cleanup-failed";

    /// <summary>A log line: Could not clean up MySQL shadow tables: {Detail}.</summary>
    public const string LogMysqlCleanupFailed = "export.log-mysql-cleanup-failed";

    /// <summary>A log line: Could not clean up Redis shadow keys: {Detail}.</summary>
    public const string LogRedisCleanupFailed = "export.log-redis-cleanup-failed";

    /// <summary>A log line: Enum `{Enum}` label `{Label}` has value {Value}, which does not fit the uint8 a BlueprintType en.</summary>
    public const string LogUnrealEnumNotBlueprint = "export.log-unreal-enum-not-blueprint";
}
