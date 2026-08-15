using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;
using Serilog;
using Tabbit.Helpers;
using Tabbit.Targets;

namespace Tabbit.History;

/// <summary>
/// Settings for the summary target.
/// </summary>
public sealed class SummaryRecipe : IOutputRecipe
{
    /// <summary>Output directory. Created if it does not exist.</summary>
    public string Path { get; set; } = "";

    /// <summary>Name of the document, without a directory.</summary>
    public string FileName { get; set; } = "summary.json";

    /// <summary>
    /// Which side this entry is built for: `c`, `s`, or `cs`/blank for both.
    ///
    /// This decides whether the entry runs at all, as it does for every target. It does
    /// not narrow what the document says: a summary always describes everything the
    /// sheets declared, or a client build would report the server's tables as gone.
    /// </summary>
    public string TargetSide { get; set; } = "cs";

    /// <summary>
    /// How much of the commit author the written file names: `full`, `masked` or `none`.
    ///
    /// The summary is the output most likely to leave the machine it was built on - it
    /// gets committed next to generated code and handed to other teams - and a person's
    /// name with their e-mail address is personal data wherever it lands. `masked`
    /// keeps one character of each so two authors can still be told apart; `none`
    /// leaves both fields null. Only this file is affected: the history keeps the full
    /// author, because attribution is what a history is for.
    /// </summary>
    public string Author { get; set; } = "full";
}

/// <summary>How much of the commit author a written summary names.</summary>
public enum AuthorDisclosure
{
    /// <summary>Name and e-mail as the commit spells them.</summary>
    Full,

    /// <summary>One character of the name, one of the e-mail's local part.</summary>
    Masked,

    /// <summary>Neither field.</summary>
    None,
}

/// <summary>
/// Writes what a conversion produced, as the document every other view renders from.
///
/// Nothing here formats anything. The report a build leaves behind, the rows a snapshot
/// puts in the history, the JSON the API serves and the page a browser draws are all
/// this file's shape - because two renderings of one question drift and nothing
/// notices, and the answer that is wrong looks exactly like the one that is right.
/// </summary>
[TabbitTarget("summary", TargetKind.Description, Order = 10)]
public class SummaryTarget : Target<SummaryRecipe>
{
    /// <summary>
    /// camelCase names, string enums, indented, and no `\r`.
    ///
    /// The document is read by a browser as much as by this tool, and it is compared
    /// byte for byte by the regression suite - so the line ending is decided here
    /// rather than by whichever machine ran the build.
    /// </summary>
    private static readonly JsonSerializerSettings Format = new JsonSerializerSettings
    {
        Formatting = Formatting.Indented,
        ContractResolver = new DefaultContractResolver { NamingStrategy = new CamelCaseNamingStrategy() },
        Converters = { new StringEnumConverter() },
    };

    protected override void Run(TargetContext context, SummaryRecipe recipe)
    {
        // An entry left in the recipe with a blank path is switched off, as everywhere.
        if (string.IsNullOrEmpty(recipe.Path))
            return;

        // Before the model is walked, so a misspelled value is reported instead of
        // being discovered after the whole document was built.
        var disclosure = ParseAuthorDisclosure(recipe.Author);

        // The unnarrowed model, always. `context.Model` is the cut this entry's target
        // side asked for, and describing that as if it were the whole thing is how a
        // client build comes to report every server-only table as deleted.
        var document = SummaryBuilder.Build(context.FullModel, context.Commit, context);

        // On the document this entry writes, never on the model or the commit: the
        // history target builds its own document from the same commit and must keep
        // the full author, or attribution - its entire point - quietly disappears.
        ApplyAuthorDisclosure(document.Run.Commit, disclosure);

        string filename = System.IO.Path.GetFullPath(
            System.IO.Path.Combine(recipe.Path, recipe.FileName));

        Log.Information($"Writing the summary to `{filename}`");

        StagingFiles.WriteAllTextToFile(filename, Render(document));
    }

    /// <summary>The document as it is written and served.</summary>
    public static string Render(SummaryDocument document)
        => JsonConvert.SerializeObject(document, Format).Replace("\r\n", "\n") + "\n";

    /// <summary>
    /// Reads the `Author` setting, rejecting values that are not spellings of anything.
    /// </summary>
    /// <remarks>
    /// Blank is `full` rather than an error: it is what an entry written before the
    /// setting existed holds, and what deleting the line leaves behind.
    /// </remarks>
    public static AuthorDisclosure ParseAuthorDisclosure(string value)
    {
        string text = (value ?? "").Trim();
        if (text.Length == 0)
            return AuthorDisclosure.Full;

        switch (text.ToLowerInvariant())
        {
            case "full": return AuthorDisclosure.Full;
            case "masked": return AuthorDisclosure.Masked;
            case "none": return AuthorDisclosure.None;
        }

        throw new TabbitException(
            $"The summary target sets `Author` to `{text}`. " +
            "It takes `full`, `masked` or `none`.");
    }

    /// <summary>
    /// Cuts the author down to what the setting allows, on the document alone.
    /// </summary>
    public static void ApplyAuthorDisclosure(SummaryCommit commit, AuthorDisclosure disclosure)
    {
        switch (disclosure)
        {
            case AuthorDisclosure.None:
                commit.AuthorName = null;
                commit.AuthorEmail = null;
                break;

            case AuthorDisclosure.Masked:
                commit.AuthorName = Masked(commit.AuthorName);
                commit.AuthorEmail = MaskedEmail(commit.AuthorEmail);
                break;
        }
    }

    /// <summary>
    /// `서재형` → `서*`. One text element rather than one char, so a name starting
    /// with a surrogate pair is not cut in half. A single `*` on purpose: padding to
    /// the original length would state the length, which is itself a fact about the
    /// person.
    /// </summary>
    private static string? Masked(string? text)
        => string.IsNullOrEmpty(text)
            ? text
            : new System.Globalization.StringInfo(text).SubstringByTextElements(0, 1) + "*";

    /// <summary>
    /// `maxidea1024@gmail.com` → `m*@gmail.com`. The domain stays - it names an
    /// organisation, not a person, and is what makes two masked authors on different
    /// teams distinguishable.
    /// </summary>
    private static string? MaskedEmail(string? email)
    {
        if (string.IsNullOrEmpty(email))
            return email;

        int at = email.IndexOf('@');

        return at <= 0 ? Masked(email) : Masked(email.Substring(0, at)) + email.Substring(at);
    }
}
