using System.Collections.Generic;

namespace Tabbit.Validation;

/// <summary>
/// The files under a folder, by name.
/// </summary>
/// <remarks>
/// Whether an asset a sheet names exists is not a question this tool can answer - it does not know
/// what an asset is. What it can do is hand over the folder: the rule decides which extension
/// matters and what a missing one means.
/// </remarks>
public interface IFileMap
{
    /// <summary>The folder that was scanned.</summary>
    string Root { get; }

    /// <summary>The pattern it was scanned with.</summary>
    string Pattern { get; }

    /// <summary>How many files matched.</summary>
    int Count { get; }

    /// <summary>Whether a file of this name is there. Extension and case are ignored.</summary>
    bool Has(string name);

    /// <summary>The full path of one file, or null.</summary>
    string? PathOf(string name);

    /// <summary>Every name that matched, extension left off.</summary>
    IEnumerable<string> Names { get; }
}

/// <summary>
/// A read-only SQL store, by the name the recipe's `Validation.Connections` gave it.
/// </summary>
/// <remarks>
/// Statements that are not queries are refused, but treat that as a convenience rather than the
/// boundary. **The boundary is a read-only account.**
///
/// The driver is not in this surface: every member answers with a BCL collection, so a rule
/// compiles against this without MySqlConnector or Npgsql being in scope.
/// </remarks>
public interface ISqlStore
{
    /// <summary>One column of a query, as a list.</summary>
    List<T> Column<T>(string sql);

    /// <summary>The same, as a set - which is what a membership check wants.</summary>
    HashSet<T> Set<T>(string sql);

    /// <summary>The first cell of the first row.</summary>
    T? Scalar<T>(string sql);

    /// <summary>Every row, each as a name-to-value map.</summary>
    List<Dictionary<string, object?>> Rows(string sql);
}

/// <summary>A read-only Redis store, by the name the recipe gave it.</summary>
public interface IRedisStore
{
    /// <summary>Whether the key is there.</summary>
    bool Exists(string key);

    /// <summary>The value of a string key, or null.</summary>
    string? Get(string key);

    /// <summary>One field of a hash, or null.</summary>
    string? Field(string key, string field);

    /// <summary>The members of a set.</summary>
    HashSet<string> Members(string key);
}
