using System;
using System.IO;

using Google.Apis.Auth.OAuth2;

using Tabbit.Importers;
using Tabbit.Recipe;

using Xunit;

namespace Tabbit.Tests;

/// <summary>
/// Which identity a Google Sheets source authenticates as, and what a recipe that does
/// not say is answered with.
/// </summary>
/// <remarks>
/// No network here, and none needed: everything below is decided before a request is
/// made. The one thing that must not happen is a recipe naming two credentials and the
/// tool picking one - a job would then read the document as somebody it is not, and
/// nothing about the output would say so.
/// </remarks>
public class GoogleSheetsCredentialTests
{
    private const string Scope = "https://www.googleapis.com/auth/spreadsheets.readonly";

    private static readonly string[] Scopes = { Scope };

    /// <summary>
    /// A key of the right shape. The private key is a throwaway generated for this test;
    /// nothing is signed with it here, because no request is made.
    /// </summary>
    private static string ServiceAccountKey()
    {
        using var rsa = System.Security.Cryptography.RSA.Create(2048);

        string pem = "-----BEGIN PRIVATE KEY-----\\n"
            + Convert.ToBase64String(rsa.ExportPkcs8PrivateKey())
            + "\\n-----END PRIVATE KEY-----\\n";

        return "{"
            + "\"type\": \"service_account\","
            + "\"project_id\": \"a-project\","
            + "\"private_key_id\": \"0123456789abcdef\","
            + $"\"private_key\": \"{pem}\","
            + "\"client_email\": \"converter@a-project.iam.gserviceaccount.com\","
            + "\"client_id\": \"1\","
            + "\"token_uri\": \"https://oauth2.googleapis.com/token\""
            + "}";
    }

    private static RecipeModel.SourceRecipeGroup.GoogleSheetsRecipe Entry()
        => new RecipeModel.SourceRecipeGroup.GoogleSheetsRecipe { SheetsId = "a-document" };

    private static string WriteTemp(string content)
    {
        string path = Path.Combine(Path.GetTempPath(), "tabbit-" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(path, content);
        return path;
    }

    // ------------------------------------------------------------- configured

    /// <summary>
    /// An entry with its contents emptied is switched off, which is how one is commented
    /// out. That has to stay true now that there are three ways to name a credential.
    /// </summary>
    [Fact]
    public void An_entry_naming_no_credential_is_switched_off()
    {
        Assert.False(GoogleSheetsCredentials.IsConfigured(Entry()));
    }

    [Fact]
    public void An_entry_naming_any_one_credential_is_configured()
    {
        var byClientSecret = Entry();
        byClientSecret.ClientSecretFilename = "secret.json";

        var byKeyFile = Entry();
        byKeyFile.ServiceAccountKeyFile = "key.json";

        var byKeyVariable = Entry();
        byKeyVariable.ServiceAccountKeyVariable = "TABBIT_TEST_SHEETS_KEY";

        Assert.True(GoogleSheetsCredentials.IsConfigured(byClientSecret));
        Assert.True(GoogleSheetsCredentials.IsConfigured(byKeyFile));
        Assert.True(GoogleSheetsCredentials.IsConfigured(byKeyVariable));
    }

    // -------------------------------------------------------------- conflicts

    /// <summary>
    /// Two ways of naming the same key. Refused rather than ranked: a precedence rule is
    /// one more thing a reader of a recipe has to know.
    /// </summary>
    [Fact]
    public void Naming_the_key_twice_is_refused()
    {
        var entry = Entry();
        entry.ServiceAccountKeyFile = "key.json";
        entry.ServiceAccountKeyVariable = "TABBIT_TEST_SHEETS_KEY";

        var ex = Assert.Throws<TabbitException>(
            () => GoogleSheetsCredentials.Acquire(entry, "Sources.GoogleSheets[0]", Scopes, "Tabbit"));

        Assert.Equal(Tabbit.Recipe.RecipeMessages.GoogleKeyFileAndVariable, ex.MessageId);
    }

    /// <summary>
    /// The one that matters. These are different identities, and picking one silently is
    /// the failure this check exists for.
    /// </summary>
    [Fact]
    public void Naming_two_identities_is_refused()
    {
        var entry = Entry();
        entry.ServiceAccountKeyFile = "key.json";
        entry.ClientSecretFilename = "secret.json";

        var ex = Assert.Throws<TabbitException>(
            () => GoogleSheetsCredentials.Acquire(entry, "Sources.GoogleSheets[0]", Scopes, "Tabbit"));

        Assert.Equal(Tabbit.Recipe.RecipeMessages.GoogleServiceAccountAndClientSecret, ex.MessageId);
        Assert.Contains("different identities", ex.Message);
    }

    /// <summary>
    /// Reached only when the caller skipped <see cref="GoogleSheetsCredentials.IsConfigured"/>,
    /// and it still says what to write rather than what is missing.
    /// </summary>
    [Fact]
    public void Naming_no_credential_says_what_to_write()
    {
        var ex = Assert.Throws<TabbitException>(
            () => GoogleSheetsCredentials.Acquire(Entry(), "Sources.GoogleSheets[0]", Scopes, "Tabbit"));

        Assert.Equal(Tabbit.Recipe.RecipeMessages.GoogleNoCredential, ex.MessageId);
    }

    // ------------------------------------------------------------ what is not there

    [Fact]
    public void A_key_file_that_is_not_there_is_named()
    {
        var entry = Entry();
        entry.ServiceAccountKeyFile = "no/such/key.json";

        var ex = Assert.Throws<TabbitException>(
            () => GoogleSheetsCredentials.Acquire(entry, "Sources.GoogleSheets[0]", Scopes, "Tabbit"));

        Assert.Equal(Tabbit.Importers.ImportMessages.GoogleKeyFileMissing, ex.MessageId);
        Assert.Contains("no/such/key.json", ex.Message);
    }

    /// <summary>
    /// An unset variable is an error rather than an empty key, for the reason the
    /// connection strings give: carrying on fails further from the cause.
    /// </summary>
    [Fact]
    public void An_unset_key_variable_is_named()
    {
        var entry = Entry();
        entry.ServiceAccountKeyVariable = "TABBIT_TEST_KEY_THAT_IS_NOT_SET";

        var ex = Assert.Throws<TabbitException>(
            () => GoogleSheetsCredentials.Acquire(entry, "Sources.GoogleSheets[0]", Scopes, "Tabbit"));

        Assert.Equal(Tabbit.Importers.ImportMessages.GoogleKeyVariableNotSet, ex.MessageId);
        Assert.Contains("TABBIT_TEST_KEY_THAT_IS_NOT_SET", ex.Message);
    }

    // ------------------------------------------------------------ the key itself

    [Fact]
    public void A_service_account_key_file_is_read()
    {
        string path = WriteTemp(ServiceAccountKey());

        try
        {
            var entry = Entry();
            entry.ServiceAccountKeyFile = path;

            var credential = GoogleSheetsCredentials.Acquire(
                entry, "Sources.GoogleSheets[0]", Scopes, "Tabbit");

            var google = Assert.IsType<GoogleCredential>(credential);
            Assert.IsType<ServiceAccountCredential>(google.UnderlyingCredential);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// And from the environment, which is where a CI job's key comes from.
    /// </summary>
    [Fact]
    public void A_service_account_key_variable_is_read()
    {
        const string variable = "TABBIT_TEST_SHEETS_KEY";

        Environment.SetEnvironmentVariable(variable, ServiceAccountKey());

        try
        {
            var entry = Entry();
            entry.ServiceAccountKeyVariable = variable;

            var credential = GoogleSheetsCredentials.Acquire(
                entry, "Sources.GoogleSheets[0]", Scopes, "Tabbit");

            var google = Assert.IsType<GoogleCredential>(credential);
            Assert.IsType<ServiceAccountCredential>(google.UnderlyingCredential);
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, null);
        }
    }

    /// <summary>
    /// A client secret put in the service account setting. The two files look alike
    /// enough to be swapped, and left to the API this comes back as an authorization
    /// failure - which reads as a permissions problem on the document.
    /// </summary>
    [Fact]
    public void A_client_secret_in_the_key_setting_is_refused_by_name()
    {
        string path = WriteTemp(
            "{\"installed\":{\"client_id\":\"1.apps.googleusercontent.com\","
            + "\"client_secret\":\"nothing-real\","
            + "\"auth_uri\":\"https://accounts.google.com/o/oauth2/auth\","
            + "\"token_uri\":\"https://oauth2.googleapis.com/token\"}}");

        try
        {
            var entry = Entry();
            entry.ServiceAccountKeyFile = path;

            var ex = Assert.Throws<TabbitException>(
                () => GoogleSheetsCredentials.Acquire(entry, "Sources.GoogleSheets[0]", Scopes, "Tabbit"));

            Assert.Equal(Tabbit.Importers.ImportMessages.GoogleKeyUnreadable, ex.MessageId);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// The client secret path still reports a file that is not there, which is the check
    /// that was already here before there was a second way to authenticate.
    /// </summary>
    [Fact]
    public void A_client_secret_that_is_not_there_is_named()
    {
        var entry = Entry();
        entry.ClientSecretFilename = "no/such/secret.json";

        var ex = Assert.Throws<TabbitException>(
            () => GoogleSheetsCredentials.Acquire(entry, "Sources.GoogleSheets[0]", Scopes, "Tabbit"));

        Assert.Equal(Tabbit.Importers.ImportMessages.GoogleClientSecretMissing, ex.MessageId);
        Assert.Contains("no/such/secret.json", ex.Message);
    }
}
