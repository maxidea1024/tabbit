using System;
using System.IO;
using System.Text;

namespace Tabbit.History;

/// <summary>
/// The page, and the assets it is made of.
///
/// Two ways out, one renderer. `--history --format html` writes a file with the data
/// and the assets inlined, for somebody with no server to point at; `--serve` sends the
/// same page and lets it fetch. The JavaScript takes whichever it finds.
///
/// Nothing is loaded from a network. The stylesheet and the script are embedded in the
/// executable and the charts are hand-drawn SVG, because the tool is expected to run on
/// closed networks - and because a page that reaches a CDN is a page that stops working
/// the day the CDN does.
/// </summary>
internal static class HistoryView
{
    private const string StyleMarker = "<!--STYLE-->";
    private const string DataMarker = "<!--DATA-->";
    private const string ScriptMarker = "<!--SCRIPT-->";

    /// <summary>One file holding the page, its assets and its data.</summary>
    public static string SelfContained(DashboardDocument dashboard)
    {
        string json = HistoryCommand.Serialize(dashboard);

        return Shell()
            .Replace(StyleMarker, "<style>\n" + Asset("history.css") + "</style>\n")

            // In a script block of a type the browser will not execute, so the data is
            // read as data. Inlining it into the script instead would make every value
            // in the sheets a piece of JavaScript, and one apostrophe in a cell enough
            // to break the page.
            .Replace(DataMarker,
                "<script type=\"application/json\" id=\"data\">" + Escape(json) + "</script>\n")

            .Replace(ScriptMarker, "<script>\n" + Asset("history.js") + "</script>\n");
    }

    /// <summary>The page as the server sends it: assets by their own URLs.</summary>
    public static string Live()
    {
        return Shell()
            .Replace(StyleMarker, "<link rel=\"stylesheet\" href=\"history.css\">\n")
            .Replace(DataMarker, "")
            .Replace(ScriptMarker, "<script src=\"history.js\"></script>\n");
    }

    /// <summary>One embedded asset, by file name.</summary>
    public static string Asset(string name)
    {
        string resource = "Tabbit.Web." + name;

        using var stream = typeof(HistoryView).Assembly.GetManifestResourceStream(resource);

        if (stream is null)
            throw new TabbitDefectException($"Embedded resource `{resource}` is missing from the build.");

        using var reader = new StreamReader(stream, new UTF8Encoding(false));

        return reader.ReadToEnd();
    }

    /// <summary>The content type an asset is served as.</summary>
    public static string ContentTypeOf(string name)
    {
        if (name.EndsWith(".css", StringComparison.Ordinal)) return "text/css; charset=utf-8";
        if (name.EndsWith(".js", StringComparison.Ordinal)) return "text/javascript; charset=utf-8";

        return "text/html; charset=utf-8";
    }

    private static string Shell() => Asset("history.html");

    /// <summary>
    /// Makes JSON safe to sit inside a script element.
    ///
    /// An HTML parser ends a script block at the first `&lt;/script` whatever the
    /// element's type says, so a cell containing that text would cut the page in half
    /// and leave the rest of the data rendered as markup. Escaping the slash is enough
    /// and leaves the JSON valid.
    /// </summary>
    private static string Escape(string json)
        => json.Replace("</", "<\\/").Replace("<!--", "<\\!--");
}
