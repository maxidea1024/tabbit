using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using Xunit;

namespace Tabbit.Tests;

/// <summary>
/// The encryption feature's last wire, in PHP: that a project which turns encryption on can
/// read its own data through the generated PHP without editing it.
/// </summary>
/// <remarks>
/// `CsEncryptedReadTests` asks the same question of C#, and asking it twice is not
/// duplication. Opening the envelope is written once per language against no shared code,
/// and PHP's half is the one that does not carry its own cipher: it calls out to
/// ext-openssl. A reader written against a function the runtime does not have compiles,
/// lints, and passes every gate that reads an unsealed file - which is exactly what
/// happened. The reader was built on `sodium_crypto_stream_chacha20_ietf_xor`, a function
/// that does not exist, and nothing here could tell.
///
/// So this converts with a key and reads the result back through the generated accessor as
/// a consuming project would: the whole path, from the sheet to a row, through the cipher
/// the interpreter actually has.
/// </remarks>
[Collection("encrypted-tree")]
public class PhpEncryptedReadTests
{
    private const string Scenario = "encrypted";
    private const string Harness = "php-check-encrypted";

    /// <summary>The variables the recipe names, and which the converter reads the keys out of.</summary>
    private const string KeyVariable = "TABBIT_TEST_TCB_KEY";

    private const string MacKeyVariable = "TABBIT_TEST_TCB_MAC_KEY";

    /// <summary>
    /// The keys this scenario is sealed and signed with - the same constants the C# gate
    /// uses, because the two read the same files.
    /// </summary>
    /// <remarks>
    /// Constants here and nowhere else, because a test fixture's key protects nothing and
    /// pretending otherwise would cost the test its determinism. They reach the converter
    /// through the environment and the client through arguments, which is the shape a real
    /// project uses even though a real project's values would not be written down.
    /// </remarks>
    private const string Key = "00112233445566778899aabbccddeeff00112233445566778899aabbccddeeff";

    private const string MacKey = "ffeeddccbbaa99887766554433221100ffeeddccbbaa99887766554433221100";

    /// <summary>
    /// A sealed export, read back through the generated PHP accessor, gives the rows the
    /// plain export holds.
    /// </summary>
    /// <remarks>
    /// The comparison is against this same run's JSON, which is never encrypted - so what
    /// is being asserted is that the encryption round trip is invisible in the data, rather
    /// than that two runs agree with each other.
    /// </remarks>
    [Fact]
    public void A_sealed_export_reads_back_through_the_generated_php()
    {
        var settings = OpensslSettings();

        Convert();

        // That the file really is sealed. Without this the gate would still pass if the
        // recipe's key were ignored and a plaintext file written - which is the one way the
        // rest of it could be green while the feature was off, and the case this whole
        // class exists because nothing was covering.
        var written = File.ReadAllBytes(
            Path.Combine(RepoLayout.OutputDir(Scenario), "binary", "Animation.tcb"));

        Assert.Equal(1, written[8] & 1);
        Assert.Contains(written[22..38], value => value != 0);

        var result = ConformanceHarness.ReadBackPhp(Scenario, Harness, settings, Key, MacKey);

        Assert.True(result.Succeeded,
            $"The generated PHP accessor could not read the sealed export.{Environment.NewLine}{result.Output}");

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
    public void Without_the_key_the_php_reader_names_the_cause()
    {
        var settings = OpensslSettings();

        Convert();

        var result = ConformanceHarness.ReadBackPhp(Scenario, Harness, settings);

        Assert.False(result.Succeeded,
            $"The sealed export loaded without a key.{Environment.NewLine}{result.Output}");

        Assert.Contains("encrypted", result.Output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("key", result.Output, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The interpreter and its cipher, as a failure rather than as a reason to stop.
    /// </summary>
    /// <remarks>
    /// A skip here would be the same silence the defect this gate exists for survived
    /// inside: the extension was off, the guard fired ahead of the call, and the missing
    /// function was never reached. So a machine without ext-openssl fails and the message
    /// names the extension, which is this suite's rule for a missing toolchain anywhere.
    /// </remarks>
    private static string[] OpensslSettings()
    {
        Assert.True(ConformanceHarness.PhpIsAvailable(out string why),
            $"A PHP interpreter is required to check the generated PHP. {why}");

        Assert.True(ConformanceHarness.PhpOpensslIsAvailable(out string[] settings, out string reason),
            $"ext-openssl is required to read a sealed table from PHP. {reason}");

        return settings;
    }

    private static void Convert()
    {
        var conversion = TabbitRunner.Convert(
            Scenario,
            new Dictionary<string, string> { [KeyVariable] = Key, [MacKeyVariable] = MacKey });

        Assert.True(conversion.Succeeded,
            $"Conversion of `{Scenario}` failed.{Environment.NewLine}{conversion.Describe()}");
    }
}
