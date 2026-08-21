using Google.Apis.Auth.OAuth2;
using Google.Apis.Http;
using Google.Apis.Util.Store;

using System;
using System.IO;
using System.Threading;

using Serilog;

using Tabbit.Recipe;

namespace Tabbit.Importers;

/// <summary>
/// Decides how a Google Sheets source proves who it is, and produces the credential.
/// </summary>
/// <remarks>
/// Two ways, and which one a project wants follows from who runs the conversion.
///
/// An OAuth client secret authenticates a *person*: the first run opens a browser for
/// consent and caches the token under that account's profile. That is the right thing on
/// a developer's machine and the wrong thing on a build server, where it makes the
/// pipeline's access to the document a property of one employee's account - it stops
/// working when they leave, and everything the job reads is read as them.
///
/// A service account authenticates the *job*. The document is shared with its address the
/// way it would be with a person, nothing is interactive, and the key arrives from the
/// environment like every other secret this tool takes.
///
/// Separated from the importer so the rules below can be checked without a network: which
/// settings conflict, and what a missing one says.
/// </remarks>
internal static class GoogleSheetsCredentials
{
    /// <summary>Which step of a run this class's log lines belong to.</summary>
    private static Serilog.ILogger Log => LogCategory.Importing;

    /// <summary>
    /// Where a person's token is cached once they have consented.
    /// </summary>
    /// <remarks>
    /// Public because a message has to be able to name it. Adding a scope does not
    /// re-consent by itself - the cached token is used as it is, and the call needing the new
    /// scope is the only thing that fails - so the useful sentence is which file to delete,
    /// and this tool is what decides where that file goes.
    /// </remarks>
    public static string TokenStore => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Personal),
        ".credentials/sheets.googleapis.com-tabbit");

    /// <summary>
    /// Whether this entry names any credential at all.
    /// </summary>
    /// <remarks>
    /// An entry with its contents emptied is how one is commented out, and the Excel
    /// source treats a blank path the same way. Naming none of the three is that.
    /// </remarks>
    public static bool IsConfigured(RecipeModel.SourceRecipeGroup.GoogleSheetsRecipe recipe)
        => !string.IsNullOrWhiteSpace(recipe.ClientSecretFilename)
        || !string.IsNullOrWhiteSpace(recipe.ServiceAccountKeyFile)
        || !string.IsNullOrWhiteSpace(recipe.ServiceAccountKeyVariable);

    /// <summary>
    /// Builds the credential this entry asks for, rejecting a combination that does not
    /// say which one is meant.
    /// </summary>
    /// <param name="recipe">The source entry.</param>
    /// <param name="section">Dotted path of the entry, for error messages.</param>
    /// <param name="scopes">What the credential is being asked to cover.</param>
    /// <param name="applicationName">Name the token store files a personal token under.</param>
    public static IConfigurableHttpClientInitializer Acquire(
        RecipeModel.SourceRecipeGroup.GoogleSheetsRecipe recipe,
        string section,
        string[] scopes,
        string applicationName)
    {
        bool keyFile = !string.IsNullOrWhiteSpace(recipe.ServiceAccountKeyFile);
        bool keyVariable = !string.IsNullOrWhiteSpace(recipe.ServiceAccountKeyVariable);
        bool clientSecret = !string.IsNullOrWhiteSpace(recipe.ClientSecretFilename);

        // Rejected rather than ranked, the way the two encryption key settings are. A
        // precedence rule is a rule somebody has to know to read the recipe, and the
        // failure it prevents - authenticating as the wrong identity - is silent.
        if (keyFile && keyVariable)
        {
            throw new TabbitException(
                $"Recipe section `{section}` names both `ServiceAccountKeyFile` and " +
                $"`ServiceAccountKeyVariable`. Name one: the file for a key on disk, the " +
                $"variable for one held in a secret store.");
        }

        if ((keyFile || keyVariable) && clientSecret)
        {
            throw new TabbitException(
                $"Recipe section `{section}` names both a service account key and " +
                $"`ClientSecretFilename`. Those authenticate as different identities, so " +
                $"name one: the service account for an unattended run, the client secret " +
                $"for a person at a machine.");
        }

        if (keyFile || keyVariable)
            return ServiceAccount(recipe, section, scopes, keyFile);

        if (clientSecret)
            return Personal(recipe, section, scopes, applicationName);

        throw new TabbitException(
            $"Recipe section `{section}` names no credential. Set `ServiceAccountKeyFile` " +
            $"or `ServiceAccountKeyVariable` for an unattended run, or " +
            $"`ClientSecretFilename` to authenticate as the person running it.");
    }

    /// <summary>
    /// A service account key, from a file or from the environment.
    /// </summary>
    private static IConfigurableHttpClientInitializer ServiceAccount(
        RecipeModel.SourceRecipeGroup.GoogleSheetsRecipe recipe,
        string section,
        string[] scopes,
        bool fromFile)
    {
        string json;
        string origin;

        if (fromFile)
        {
            if (!File.Exists(recipe.ServiceAccountKeyFile))
            {
                throw new TabbitException(
                    $"Recipe section `{section}` names service account key file " +
                    $"`{recipe.ServiceAccountKeyFile}`, which does not exist.");
            }

            json = File.ReadAllText(recipe.ServiceAccountKeyFile);
            origin = $"file `{recipe.ServiceAccountKeyFile}`";
        }
        else
        {
            string? value = Environment.GetEnvironmentVariable(recipe.ServiceAccountKeyVariable);

            // The same reasoning the connection strings use: an unset variable is an
            // error rather than an empty substitution, because carrying on produces a
            // failure further away from its cause.
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new TabbitException(
                    $"Recipe section `{section}` reads its service account key from " +
                    $"environment variable `{recipe.ServiceAccountKeyVariable}`, which is " +
                    $"not set. The key is held in the environment so it need not be committed.");
            }

            json = value;
            origin = $"environment variable `{recipe.ServiceAccountKeyVariable}`";
        }

        ServiceAccountCredential serviceAccount;

        // Asked for by type, so a file that is not a service account key is refused here
        // rather than at the first request. The two JSON files Google hands out look
        // alike enough to be swapped, and a client secret put here would otherwise come
        // back as an authorization failure - which reads as a permissions problem on the
        // document, and sends the search to the wrong place.
        try
        {
            serviceAccount = CredentialFactory.FromJson<ServiceAccountCredential>(json);
        }
        catch (Exception ex) when (ex is not TabbitDefectException)
        {
            throw new TabbitException(
                $"Recipe section `{section}` could not read a service account key from " +
                $"{origin}: {ex.Message} A service account key has " +
                $"`\"type\": \"service_account\"`; an OAuth client secret goes in " +
                $"`ClientSecretFilename` instead.");
        }

        Log.Debug($"Google Sheets source `{section}` authenticates as a service account, from {origin}.");

        return serviceAccount.ToGoogleCredential().CreateScoped(scopes);
    }

    /// <summary>
    /// The interactive flow: consent once, then a cached token under the user's profile.
    /// </summary>
    private static IConfigurableHttpClientInitializer Personal(
        RecipeModel.SourceRecipeGroup.GoogleSheetsRecipe recipe,
        string section,
        string[] scopes,
        string applicationName)
    {
        if (!File.Exists(recipe.ClientSecretFilename))
        {
            throw new TabbitException(
                $"Recipe section `{section}` names client secret file " +
                $"`{recipe.ClientSecretFilename}`, which does not exist.");
        }

        using var stream = new FileStream(recipe.ClientSecretFilename, FileMode.Open, FileAccess.Read);

        string credentialsPath = TokenStore;

        var clientSecrets = GoogleClientSecrets.FromStream(stream);

        Log.Debug(
            $"Google Sheets source `{section}` authenticates as the person running it; " +
            $"the token is cached under `{credentialsPath}`.");

        return GoogleWebAuthorizationBroker.AuthorizeAsync(
            clientSecrets.Secrets,
            scopes,
            // Fixed rather than the machine's user name, which would ask for consent
            // again the first time the conversion ran under a different account.
            "TabbitUser",
            CancellationToken.None,
            new FileDataStore(credentialsPath, true)).Result;
    }
}
