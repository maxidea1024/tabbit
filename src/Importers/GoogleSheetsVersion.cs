using System;
using System.Net;
using System.Net.Http;
using Google.Apis.Http;
using Google.Apis.Sheets.v4;
using Google.Apis.Services;
using Newtonsoft.Json.Linq;
using Serilog;
using Tabbit.Recipe;
using Tabbit.Messages;

namespace Tabbit.Importers;

/// <summary>
/// What Drive says a document's version is, so a run can find out whether it changed without
/// fetching it.
/// </summary>
/// <remarks>
/// A hosted document has no size and no modification time this tool can read, so the only way
/// to answer "did this change" cheaply is to ask the service. Drive answers with `version`, a
/// counter it increases on every change made on the server.
///
/// **`version` rather than `modifiedTime`.** A timestamp brings back the one weakness the
/// file comparison has - two states that look alike - across the network, where there is no
/// second check to fall back on. A counter has no such state. It also moves for changes
/// nobody can see, which costs an occasional conversion that did not need to happen: the safe
/// direction.
///
/// **Not `headRevisionId`.** Drive documents that for files with binary content, which a
/// spreadsheet created in Sheets is not.
///
/// **Through a plain request rather than the Drive client library.** One field of one
/// document is the whole of what is wanted, and taking a dependency on another API package to
/// read it would be a large amount of assembly for a two-line question.
/// </remarks>
internal static class GoogleSheetsVersion
{
    /// <summary>Which step of a run this class's log lines belong to.</summary>
    private static ILogger Log => LogCategory.Caching;

    /// <summary>Metadata-only, and the narrowest scope that answers this.</summary>
    public const string Scope = "https://www.googleapis.com/auth/drive.metadata.readonly";

    /// <summary>
    /// Whether the tool has already explained what to grant.
    /// </summary>
    /// <remarks>
    /// Once per run, however many documents a recipe reads. Five documents refused for one
    /// reason are one thing to do about it, and five copies of the instructions bury the
    /// instruction.
    /// </remarks>
    private static bool _explained;

    /// <summary>
    /// Reads a document's version, or null when it cannot be read.
    /// </summary>
    /// <remarks>
    /// Never throws for a reason outside this tool's control. Not being allowed to ask, or
    /// the service not being reachable, means the document has to be fetched to find out
    /// whether it changed - which is what the run would have done anyway. The cost is one
    /// slow run, and the point of the message is that it does not have to stay that way.
    /// </remarks>
    public static string? Read(RecipeModel.SourceRecipeGroup.GoogleSheetsRecipe recipe, string section)
    {
        try
        {
            var credential = GoogleSheetsCredentials.Acquire(
                recipe, section, [SheetsService.Scope.SpreadsheetsReadonly, Scope], "Tabbit");

            // Built by hand rather than taken from a service class, because there is no
            // service class for one metadata field. The credential goes in as an initializer,
            // which is what a generated client does with it too.
            var arguments = new CreateHttpClientArgs { ApplicationName = "Tabbit" };
            arguments.Initializers.Add(credential);

            using var service = new HttpClientFactory().CreateHttpClient(arguments);

            string url =
                $"https://www.googleapis.com/drive/v3/files/{Uri.EscapeDataString(recipe.SheetsId)}"
                + "?fields=version%2CmodifiedTime&supportsAllDrives=true";

            using var response = service.GetAsync(url).GetAwaiter().GetResult();

            if (!response.IsSuccessStatusCode)
            {
                Refused(recipe, response);
                return null;
            }

            string body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();

            string? version = (string?)JObject.Parse(body)["version"];

            if (string.IsNullOrEmpty(version))
            {
                Log.Debug($"Drive answered for `{recipe.SheetsId}` without a version field.");
                return null;
            }

            return version;
        }
        catch (Exception ex)
        {
            // A transient failure - no network, a 5xx behind the client library - is not
            // something to act on, so it does not get the treatment above. The run imports
            // the document, which is what it would have done before any of this existed.
            Log.Information(
                $"Could not read the version of document `{recipe.SheetsId}`: {ex.Message}. Importing it.");

            return null;
        }
    }

    /// <summary>
    /// Says why the request was refused, and what would make it work.
    /// </summary>
    /// <remarks>
    /// The three refusals have three different remedies and only one of them is this tool's
    /// doing, so they are told apart rather than reported as one. Where they cannot be told
    /// apart, all of the candidates are named: three things to check beats "403".
    /// </remarks>
    private static void Refused(
        RecipeModel.SourceRecipeGroup.GoogleSheetsRecipe recipe, HttpResponseMessage response)
    {
        string document = recipe.SheetsId;

        if (response.StatusCode != HttpStatusCode.Forbidden &&
            response.StatusCode != HttpStatusCode.Unauthorized)
        {
            Log.Information(
                $"Could not read the version of document `{document}`: Drive answered "
                + $"{(int)response.StatusCode}. Importing it.");

            return;
        }

        string body = "";

        try
        {
            body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        }
        catch (Exception)
        {
            // The status is the part that matters; a body that cannot be read only costs
            // the ability to tell the three cases apart, which the fallback below covers.
        }

        bool serviceAccount = !string.IsNullOrWhiteSpace(recipe.ServiceAccountKeyFile)
                           || !string.IsNullOrWhiteSpace(recipe.ServiceAccountKeyVariable);

        Log.Warning(Message.Of(ImportMessages.LogVersionUnreadable,
            ("Document", document)).In(MessageCatalog.Current));

        if (_explained)
            return;

        _explained = true;

        if (body.Contains("accessNotConfigured", StringComparison.OrdinalIgnoreCase) ||
            body.Contains("SERVICE_DISABLED", StringComparison.OrdinalIgnoreCase))
        {
            Log.Warning(Message.Of(ImportMessages.LogDriveApiDisabled).In(MessageCatalog.Current));

            return;
        }

        if (serviceAccount)
        {
            Log.Warning(Message.Of(ImportMessages.LogScopeNotAllowed,
                ("Scope", Scope)).In(MessageCatalog.Current));

            return;
        }

        Log.Warning(Message.Of(ImportMessages.LogCachedTokenPredatesScope,
            ("Scope", Scope),
            ("TokenStore", GoogleSheetsCredentials.TokenStore)).In(MessageCatalog.Current));

        Log.Warning(Message.Of(ImportMessages.LogUntilGrantedImportsEverything).In(MessageCatalog.Current));
    }
}
