using Xunit;

namespace Tabbit.Tests;

/// <summary>
/// The masking that golden comparison depends on.
///
/// Golden trees are recorded masked and compared masked, so a mask that changes its own
/// output writes a golden that nothing can ever match - including the very output it
/// was recorded from. That failure looks like a real regression in whatever the mask
/// touched, which is a long way from where the problem is.
/// </summary>
public class NormalizerTests
{
    private const string Page =
        "<div>x</div>\n</body></html>\n";

    private const string Summary =
        "{\n  \"schemaVersion\": 1,\n" +
        "  \"run\": { \"generatedAt\": \"2026-08-03T12:05:46.1234567+09:00\", \"toolVersion\": \"1.0.0.0\" },\n" +
        "  \"data\": { \"hash\": \"abc\", \"totals\": { \"tables\": 2 } }\n}\n";

    private const string Manifest =
        "{ \"LastUpdatedDate\": \"2026-08-03T12:05:46.1234567+09:00\", \"TotalSize\": 12 }\n";

    [Theory]
    [InlineData("html/index.html", Page)]
    [InlineData("summary/summary.json", Summary)]
    [InlineData("binary/manifest-binary.json", Manifest)]
    [InlineData("csharp/Accessor.cs", "public class Accessor { }\n")]
    public void Masking_twice_is_the_same_as_masking_once(string path, string content)
    {
        string once = OutputNormalizer.Normalize(path, content);

        Assert.Equal(once, OutputNormalizer.Normalize(path, once));
    }

    /// <summary>
    /// A page is compared as it was written, line endings apart.
    ///
    /// It carried a footer with the wall clock until 2026-09-01 - and the machine's
    /// account name before that - and this file masked both. Neither is written any more,
    /// so the mask went with them and every byte of a page is now compared.
    /// </summary>
    [Fact]
    public void A_page_is_left_alone()
    {
        Assert.Equal(Page, OutputNormalizer.Normalize("html/index.html", "<div>x</div>\r\n</body></html>\r\n"));
    }

    /// <summary>
    /// A summary's `run` block is the clock, the machine and the commit; its `data` is
    /// the whole point of comparing the file at all.
    /// </summary>
    [Fact]
    public void A_summarys_run_is_masked_and_its_data_is_not()
    {
        string masked = OutputNormalizer.Normalize("summary/summary.json", Summary);

        Assert.DoesNotContain("1.0.0.0", masked);
        Assert.DoesNotContain("2026-08-03", masked);

        Assert.Contains("\"hash\": \"abc\"", masked);
        Assert.Contains("\"tables\": 2", masked);
    }

    /// <summary>
    /// A file that is not the JSON it is named after is compared as text rather than
    /// swallowed, so the problem is reported instead of masked.
    /// </summary>
    [Fact]
    public void Something_that_is_not_json_is_left_alone()
    {
        const string broken = "{ this is not json";

        Assert.Equal(broken, OutputNormalizer.Normalize("summary/summary.json", broken));
    }
}
