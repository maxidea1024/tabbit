using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using Xunit;

namespace Tabbit.Tests;

/// <summary>
/// The last wire of the encryption feature: that a project which turns encryption on can
/// read its own data without editing the generated code.
/// </summary>
/// <remarks>
/// Everything either side of this was already covered - `ChaCha20Tests` pins the cipher and
/// the envelope against the shipped reader, and the golden trees pin what the generator
/// writes. What neither could answer is whether the two meet: the accessor loads bytes and
/// something has to open them before a reader sees them, and for as long as nothing did, a
/// sealed export was a build that produced files its own client could not load.
///
/// So this converts with a key, and reads the result back through the generated accessor as
/// a consuming project would - the whole path, from the sheet to a row.
/// </remarks>
public class CsEncryptedReadTests
{
    private const string Scenario = "encrypted";
    private const string Harness = "cs-check-encrypted";

    /// <summary>The variables the recipe names, and which the converter reads the keys out of.</summary>
    private const string KeyVariable = "TABBIT_TEST_TCB_KEY";

    private const string MacKeyVariable = "TABBIT_TEST_TCB_MAC_KEY";

    /// <summary>
    /// The keys this scenario is sealed and signed with.
    /// </summary>
    /// <remarks>
    /// Constants here and nowhere else, because a test fixture's key protects nothing and
    /// pretending otherwise would cost the test its determinism. They reach the converter
    /// through the environment and the client through arguments, which is the shape a real
    /// project uses even though a real project's values would not be written down.
    ///
    /// Two different keys, which is also what the converter requires: one secret doing the
    /// work of two is refused before the first table is written.
    /// </remarks>
    private const string Key = "00112233445566778899aabbccddeeff00112233445566778899aabbccddeeff";

    private const string MacKey = "ffeeddccbbaa99887766554433221100ffeeddccbbaa99887766554433221100";

    /// <summary>
    /// A sealed export, read back through the generated accessor, gives the rows the plain
    /// export holds.
    /// </summary>
    /// <remarks>
    /// The comparison is against this same run's JSON, which is never encrypted - so what is
    /// being asserted is that the encryption round trip is invisible in the data, rather than
    /// that two runs agree with each other.
    /// </remarks>
    [Fact]
    public void A_sealed_export_reads_back_through_the_generated_accessor()
    {
        Convert();

        // That the file really is sealed and really is signed. Without this the test would
        // still pass if the recipe's keys were ignored and a plain file written - which is
        // the one way the rest of it could be green while the feature was off.
        var written = File.ReadAllBytes(TableFile);

        Assert.Equal(1, written[8] & 1);
        Assert.Contains(written[22..38], value => value != 0);

        var result = CsToolchain.ReadBack(Scenario, Harness, Key, MacKey);

        Assert.True(result.Succeeded,
            $"The generated accessor could not read the sealed export.{Environment.NewLine}{result.Output}");

        var actual = JsonDocument.Parse(result.StdOut).RootElement;
        var expected = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(RepoLayout.OutputDir(Scenario), "json", "Animation.json"))).RootElement;

        Assert.Equal(expected.GetArrayLength(), actual.GetArrayLength());
        Assert.True(expected.GetArrayLength() > 0, "The fixture exported no rows to compare.");

        for (int at = 0; at < expected.GetArrayLength(); at++)
        {
            Assert.Equal(expected[at].GetProperty("index").GetString(),
                         actual[at].GetProperty("index").GetString());

            Assert.Equal(expected[at].GetProperty("slot").GetInt32(),
                         actual[at].GetProperty("slot").GetInt32());

            // Printed round-trippably and compared as the type the column is, so the
            // assertion is about the value rather than about how JSON renders a float.
            Assert.Equal(
                (float)expected[at].GetProperty("blend").GetDouble(),
                float.Parse(actual[at].GetProperty("blend").GetString(),
                            CultureInfo.InvariantCulture));
        }
    }

    /// <summary>
    /// The same accessor, the same files, no key - and the reader says which of those is
    /// the problem.
    /// </summary>
    /// <remarks>
    /// The failure a project meets first if it turns encryption on and forgets to set the
    /// key, so the message it gets is part of the feature. What it must not be is a
    /// structural complaint about ciphertext, which is what reading the bytes without
    /// opening the envelope would produce.
    /// </remarks>
    [Fact]
    public void Without_the_key_the_reader_names_the_cause()
    {
        Convert();

        var result = CsToolchain.ReadBack(Scenario, Harness);

        Assert.False(result.Succeeded,
            $"The sealed export loaded without a key.{Environment.NewLine}{result.Output}");

        Assert.Contains("encrypted", result.Output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("key", result.Output, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A file edited after it was exported does not load, through the whole path.
    /// </summary>
    /// <remarks>
    /// The reason the MAC exists, asserted where a project would meet it. The edit is four
    /// bytes of the body, which is what changing a value looks like - not a truncation and
    /// not a length, because those the structural checks already catch. Nothing about the
    /// file's shape changes, so before the MAC this was a file that loaded and gave different
    /// numbers.
    ///
    /// The edit is made through the ciphertext, without the key, because that is the property
    /// a stream cipher has: the same bit flips in the value it decrypts to.
    /// </remarks>
    [Fact]
    public void An_edited_file_is_refused_by_the_generated_accessor()
    {
        Convert();

        var bytes = File.ReadAllBytes(TableFile);

        // Well past the header, in the middle of the blocks.
        for (int at = 0; at < 4; at++)
            bytes[bytes.Length - 12 + at] ^= 0xFF;

        File.WriteAllBytes(TableFile, bytes);

        var result = CsToolchain.ReadBack(Scenario, Harness, Key, MacKey);

        Assert.False(result.Succeeded,
            $"An edited file loaded.{Environment.NewLine}{result.Output}");

        Assert.Contains("MAC", result.Output, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The same edited file, read by a build with no MAC key, loads and gives other values.
    /// </summary>
    /// <remarks>
    /// The other half of the pair above, and the measurement the whole feature rests on: the
    /// structural checks pass on this file. Without it the previous test would show that a
    /// MAC refuses something, without showing that anything else would have accepted it.
    /// </remarks>
    [Fact]
    public void The_same_edit_passes_every_check_that_is_not_the_mac()
    {
        Convert();

        var original = CsToolchain.ReadBack(Scenario, Harness, Key);
        Assert.True(original.Succeeded, original.Output);

        var bytes = File.ReadAllBytes(TableFile);

        for (int at = 0; at < 4; at++)
            bytes[bytes.Length - 12 + at] ^= 0xFF;

        File.WriteAllBytes(TableFile, bytes);

        var edited = CsToolchain.ReadBack(Scenario, Harness, Key);

        Assert.True(edited.Succeeded,
            $"The edit was caught by something other than the MAC, which makes the pair of "
            + $"tests around it meaningless.{Environment.NewLine}{edited.Output}");

        Assert.NotEqual(original.StdOut, edited.StdOut);
    }

    /// <summary>The one file this scenario exports.</summary>
    private static string TableFile
        => Path.Combine(RepoLayout.OutputDir(Scenario), "binary", "Animation.tcb");

    private static void Convert()
    {
        var conversion = TabbitRunner.Convert(
            Scenario,
            new Dictionary<string, string> { [KeyVariable] = Key, [MacKeyVariable] = MacKey });

        Assert.True(conversion.Succeeded,
            $"Conversion of `{Scenario}` failed.{Environment.NewLine}{conversion.Describe()}");
    }
}
