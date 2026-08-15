namespace Tabbit.Validation;

/// <summary>
/// When a folder's rules run, and what they are given.
/// </summary>
public enum RuleStage
{
    /// <summary>Before anything is read: file names, settings, environment.</summary>
    Pre,

    /// <summary>One table each, named by the file.</summary>
    Table,

    /// <summary>The whole model: rules across tables, and conventions over all of them.</summary>
    Global,

    /// <summary>The same, plus read-only access to an external store.</summary>
    Runtime,
}
