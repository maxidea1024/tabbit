using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Tabbit.Exporters;

/// <summary>
/// Resolves the connection string of a database export target.
///
/// Recipes are committed to version control, so a password written into one ends
/// up in history - which is exactly how this repository leaked a Google OAuth
/// secret. Connection strings therefore support `${NAME}` placeholders that are
/// filled from the environment at run time:
///
///     "ConnectionString": "Server=db;Database=game;Uid=tabbit;Pwd=${DB_PASSWORD}"
///
/// A recipe can then be committed in full while the secret stays in CI settings or
/// a developer's shell. An unset variable is an error rather than an empty
/// substitution, because silently connecting with a blank password fails later and
/// less clearly.
/// </summary>
public static class ConnectionString
{
    private static readonly Regex Placeholder = new Regex(@"\$\{(?<name>[A-Za-z_][A-Za-z0-9_]*)\}", RegexOptions.Compiled);

    /// <summary>
    /// Expands every `${NAME}` placeholder from the environment.
    /// </summary>
    /// <param name="template">Connection string as written in the recipe.</param>
    /// <param name="recipeSection">Dotted path of the recipe section, for error messages.</param>
    public static string Resolve(string? template, string? recipeSection)
    {
        if (string.IsNullOrWhiteSpace(template))
        {
            throw new TabbitException(
                $"Recipe section `{recipeSection}` has no ConnectionString.");
        }

        var missing = new List<string>();

        string resolved = Placeholder.Replace(template, match =>
        {
            string name = match.Groups["name"].Value;
            string? value = Environment.GetEnvironmentVariable(name);

            if (string.IsNullOrEmpty(value))
            {
                missing.Add(name);
                return match.Value;
            }

            return value;
        });

        if (missing.Count > 0)
        {
            throw new TabbitException(
                $"Recipe section `{recipeSection}` refers to environment variable(s) " +
                $"{string.Join(", ", missing)}, which are not set. " +
                $"Connection secrets are read from the environment so they need not be committed.");
        }

        return resolved;
    }

    /// <summary>
    /// A connection string with any password-like value masked, safe to log.
    ///
    /// Diagnostics name the target they were working against, and a connection
    /// string is the natural way to say which - but it is also the one string in
    /// the process most likely to hold a credential.
    /// </summary>
    public static string Redact(string connectionString)
    {
        if (string.IsNullOrEmpty(connectionString))
            return "";

        var masked = new StringBuilder();

        foreach (var part in connectionString.Split(';'))
        {
            if (part.Length == 0)
                continue;

            if (masked.Length > 0)
                masked.Append(';');

            int equals = part.IndexOf('=');
            if (equals <= 0)
            {
                masked.Append(part);
                continue;
            }

            string key = part.Substring(0, equals).Trim();

            if (IsSecretKey(key))
                masked.Append(key).Append("=***");
            else
                masked.Append(part);
        }

        return masked.ToString();
    }

    private static bool IsSecretKey(string key)
    {
        switch (key.ToLowerInvariant())
        {
            case "password":
            case "pwd":
            case "user password":
            case "accesstoken":
            case "access token":
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// Masks the credential portion of a URI-style connection string, such as
    /// MongoDB's `mongodb://user:pass@host`.
    /// </summary>
    public static string RedactUri(string uri)
    {
        if (string.IsNullOrEmpty(uri))
            return "";

        return Regex.Replace(uri, @"://[^/@]*:([^/@]*)@", "://***:***@");
    }
}
