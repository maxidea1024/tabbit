using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Tabbit.Tests;

/// <summary>
/// Masks the parts of Tabbit's output that legitimately change between runs, so
/// golden comparison reacts to behaviour changes rather than to the clock.
///
/// Two things are non-deterministic today:
///
///   * manifest files stamp DateTime.Now on the manifest and on every item
///   * a summary's `run` block holds the clock, the tool version and the commit
///
/// Binary tables, the generated pages and the generated C#/TypeScript are already
/// byte-stable. The pages were the third entry here until the footer that carried the
/// wall clock - and the machine's user name before that - was dropped: a generated page
/// gets committed, so a per-run value in it made every regeneration a diff.
///
/// This runs when a golden tree is recorded as well as when one is compared, so what
/// is committed is the masked text rather than one machine's copy of the volatile
/// parts. Nothing is lost - the same function decides both sides - and it keeps a
/// developer's user name, and the commit that happened to be checked out the day a
/// golden was recorded, out of the repository.
///
/// Recording masked text makes idempotence a requirement rather than a nicety: the
/// golden is normalized once when written and again on every comparison, so a mask
/// that changes its own output produces a golden nothing can ever match.
/// <see cref="NormalizerTests"/> holds it to that.
/// </summary>
internal static class OutputNormalizer
{
    private static readonly Regex IsoTimestamp = new Regex(
        @"\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(\.\d+)?([+-]\d{2}:\d{2}|Z)",
        RegexOptions.Compiled);

    public static string Normalize(string relativePath, string content)
    {
        string path = relativePath.Replace('\\', '/');

        content = content.Replace("\r\n", "\n");

        if (path.Contains("manifest"))
            content = IsoTimestamp.Replace(content, "<TIMESTAMP>");

        if (path.EndsWith("summary.json"))
            content = MaskRun(content);

        return content;
    }

    /// <summary>
    /// Replaces a summary's `run` block, keeping its `data` exactly.
    ///
    /// Structurally rather than by pattern, because what is volatile is every field of
    /// one object rather than one recognisable shape - and because the split exists
    /// precisely so that `data` can be held to byte equality. A regex over the whole
    /// file would eventually mask something in `data` too, and a masked difference is
    /// a check that stopped checking.
    /// </summary>
    private static string MaskRun(string content)
    {
        JObject document;

        try
        {
            document = JObject.Parse(content);
        }
        catch (JsonException)
        {
            // Not a summary after all. Compared as text, which will report the
            // difference rather than hiding it.
            return content;
        }

        if (document["run"] != null)
            document["run"] = "<RUN>";

        return document.ToString(Formatting.Indented).Replace("\r\n", "\n") + "\n";
    }

    /// <summary>
    /// Files compared byte for byte rather than as normalized text.
    /// </summary>
    public static bool IsBinary(string relativePath)
        => relativePath.EndsWith(".tcb");
}
