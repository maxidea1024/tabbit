using System;
using System.Security.Cryptography;

using Tabbit.Exporters;

using Xunit;

namespace Tabbit.Tests;

/// <summary>
/// The MAC, and the reason the format has one.
/// </summary>
/// <remarks>
/// The claim this feature rests on is that the structural checks cannot see an edited value.
/// It is asserted here rather than assumed - the first test alters a file and reads the
/// altered value back through the shipped reader with no complaint - because if it were false
/// the whole layer would be cost without a purpose, and because it is the kind of claim that
/// quietly stops being true when the checks get stricter.
///
/// Every reader runtime carries its own implementation of this tag. The vector below is the
/// description each of those ports is written against: a fixed file and a fixed key produce
/// sixteen fixed bytes, and a port that reproduces them agrees about the algorithm, the
/// covered range and the truncation all at once.
/// </remarks>
public class TcbMacTests
{
    private static readonly byte[] MacKey = Convert.FromHexString(
        "404142434445464748494a4b4c4d4e4f505152535455565758595a5b5c5d5e5f");

    /// <summary>
    /// A file with four bytes changed reads back as different data, and nothing objects.
    /// </summary>
    /// <remarks>
    /// The measurement the feature is built on. A block length that does not add up is a
    /// malformed file and the reader says so; four other bytes in a fixed-width column is a
    /// well-formed file holding a different number, and no check over a file's shape can tell
    /// that from data that was always there.
    /// </remarks>
    [Fact]
    public void Without_a_mac_an_edited_value_is_read_as_though_it_belonged()
    {
        var file = TcbFiles.Plain(128);
        var edited = (byte[])file.Clone();

        // Four bytes in the body, as an edit to a fixed-width value would be.
        for (int at = 0; at < 4; at++)
            edited[TcbFormat.HeaderSize + 40 + at] ^= 0xFF;

        var opened = Tabbit.Binary.TcbTable.Open(edited, null).ToArray();

        Assert.Equal(edited, opened);
        Assert.NotEqual(file, opened);
    }

    /// <summary>The same edit, on a file that carries a MAC, does not load.</summary>
    [Fact]
    public void An_edited_value_is_refused_when_the_file_carries_a_mac()
    {
        var file = TcbFiles.Plain(128);
        TcbMac.Sign(file, MacKey);

        file[TcbFormat.HeaderSize + 40] ^= 0x01;

        var failure = Assert.Throws<Tabbit.Binary.TcbException>(
            () => Tabbit.Binary.TcbTable.Open(file, null, MacKey));

        Assert.Contains("altered", failure.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A bit flipped in the ciphertext is refused - which is a bit flipped in the value.
    /// </summary>
    /// <remarks>
    /// Why encryption on its own was never tamper resistance. The envelope is a keystream
    /// XOR, so this edit needs no key at all: it flips exactly the same bit of the plaintext,
    /// the file still decrypts, the key check still passes and the structure still adds up.
    /// </remarks>
    [Fact]
    public void A_flipped_ciphertext_bit_is_refused()
    {
        var key = new byte[32];

        var file = TcbFiles.Plain(128);
        TcbEnvelope.Seal(file, key);
        TcbMac.Sign(file, MacKey);

        file[TcbFormat.HeaderSize + 8] ^= 0x01;

        Assert.Throws<Tabbit.Binary.TcbException>(
            () => Tabbit.Binary.TcbTable.Open(file, key, MacKey));
    }

    /// <summary>
    /// The header is covered too: the nonce, the flags and the version.
    /// </summary>
    /// <remarks>
    /// What encrypt-then-MAC buys beyond ordering. A tag over the plaintext would leave every
    /// one of these free to be changed underneath it - and a swapped nonce turns a file into
    /// different values without touching a byte of the body.
    /// </remarks>
    [Theory]
    [InlineData(TcbFormat.VersionOffset)]
    [InlineData(TcbFormat.FlagsOffset)]
    [InlineData(TcbFormat.NonceOffset)]
    [InlineData(TcbFormat.NonceOffset + 11)]
    public void The_header_is_covered(int offset)
    {
        var key = new byte[32];

        var file = TcbFiles.Plain(128);
        TcbEnvelope.Seal(file, key);
        TcbMac.Sign(file, MacKey);

        file[offset] ^= 0x01;

        Assert.False(TcbMac.Verify(file, MacKey));
    }

    /// <summary>
    /// Zeroing the tag does not remove the check from a reader that holds a key.
    /// </summary>
    /// <remarks>
    /// The whole feature rests on this one. "All zero means no MAC" is how a file says it is
    /// unauthenticated, and if a reader with a key accepted that, stripping the protection
    /// would be sixteen zero bytes of work. So the question the reader asks is whether the
    /// project uses MACs - which is whether it was given a key - not whether this file
    /// happens to carry one.
    /// </remarks>
    [Fact]
    public void A_stripped_mac_is_refused_by_a_reader_that_has_a_key()
    {
        var file = TcbFiles.Plain(128);
        TcbMac.Sign(file, MacKey);

        for (int at = 0; at < TcbFormat.MacSize; at++)
            file[TcbFormat.MacOffset + at] = 0;

        var failure = Assert.Throws<Tabbit.Binary.TcbException>(
            () => Tabbit.Binary.TcbTable.Open(file, null, MacKey));

        Assert.Contains("no MAC", failure.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A file signed with another key is refused.</summary>
    [Fact]
    public void A_file_signed_with_another_key_is_refused()
    {
        var other = new byte[32];
        other[0] = 1;

        var file = TcbFiles.Plain(128);
        TcbMac.Sign(file, other);

        Assert.Throws<Tabbit.Binary.TcbException>(
            () => Tabbit.Binary.TcbTable.Open(file, null, MacKey));
    }

    /// <summary>
    /// A project that does not use MACs reads a file that has none, and one that has one.
    /// </summary>
    /// <remarks>
    /// The second half is the deployment case this format has promised to keep working: a
    /// client that shipped before MACs existed, sent data exported after they did. It cannot
    /// check the tag and is no worse off than it was, so it reads the file. Refusing would
    /// turn turning the feature on into a break for every client already out there.
    /// </remarks>
    [Fact]
    public void A_reader_without_a_key_reads_either_kind_of_file()
    {
        var plain = TcbFiles.Plain(128);
        Assert.Equal(plain, Tabbit.Binary.TcbTable.Open(plain, null).ToArray());

        var signed = TcbFiles.Plain(128);
        TcbMac.Sign(signed, MacKey);

        Assert.Equal(signed, Tabbit.Binary.TcbTable.Open(signed, null).ToArray());
    }

    /// <summary>The check can be switched off, and then an altered file loads.</summary>
    [Fact]
    public void The_check_can_be_skipped()
    {
        var file = TcbFiles.Plain(128);
        TcbMac.Sign(file, MacKey);

        file[TcbFormat.HeaderSize + 40] ^= 0x01;

        Assert.Throws<Tabbit.Binary.TcbException>(
            () => Tabbit.Binary.TcbTable.Open(file, null, MacKey));

        var opened = Tabbit.Binary.TcbTable.Open(file, null, MacKey, verifyMac: false);

        Assert.Equal(file, opened.ToArray());
    }

    /// <summary>
    /// A fixed file and a fixed key give sixteen fixed bytes.
    /// </summary>
    /// <remarks>
    /// The cross-language vector. Each of the thirteen runtimes asserts this same constant,
    /// which pins three things a port can get wrong independently: the algorithm, which bytes
    /// of the file are covered, and that the tag is the leading half of the digest rather
    /// than the trailing one.
    ///
    /// The file it is taken over is reproducible anywhere: a plain header - the signature,
    /// version 105, then twenty-nine zeros and the signature again at the key check - and a
    /// body of two hundred bytes where byte `i` is `(i * 31) % 253`. The key is 0x40 to 0x5f.
    ///
    /// **The version in that header is written here rather than taken from the format.** The
    /// vector pins the algorithm, the covered bytes and which half of the digest is kept -
    /// none of which the format version has anything to do with. Taken from the constant, it
    /// would move every time the format did, and a published constant that moves is one
    /// nobody can check against.
    /// </remarks>
    [Fact]
    public void The_tag_is_the_published_vector()
    {
        var file = TcbFiles.Plain(200);

        BitConverter.TryWriteBytes(file.AsSpan(TcbFormat.VersionOffset), 105u);

        TcbMac.Sign(file, MacKey);

        Assert.Equal(
            "2d8298b1e59598105580e9a61d685a3f",
            Convert.ToHexString(file.AsSpan(TcbFormat.MacOffset, TcbFormat.MacSize))
                   .ToLowerInvariant());
    }

    /// <summary>
    /// The tag is HMAC-SHA-256 over the file with its own sixteen bytes left out.
    /// </summary>
    /// <remarks>
    /// Against an independent computation rather than against itself. The HMAC comes from the
    /// platform here, so what can be wrong in this runtime is not the algorithm but the range
    /// it covers - and this states that range a second way.
    /// </remarks>
    [Fact]
    public void The_tag_covers_every_byte_but_its_own()
    {
        var file = TcbFiles.Plain(200);
        TcbMac.Sign(file, MacKey);

        var covered = new byte[file.Length - TcbFormat.MacSize];

        Array.Copy(file, 0, covered, 0, TcbFormat.MacOffset);
        Array.Copy(
            file, TcbFormat.KeyCheckOffset,
            covered, TcbFormat.MacOffset, file.Length - TcbFormat.KeyCheckOffset);

        using var hmac = new HMACSHA256(MacKey);

        Assert.Equal(
            hmac.ComputeHash(covered).AsSpan(0, TcbFormat.MacSize).ToArray(),
            file.AsSpan(TcbFormat.MacOffset, TcbFormat.MacSize).ToArray());
    }

    /// <summary>The same file signs to the same bytes, which the golden trees rest on.</summary>
    [Fact]
    public void Signing_is_deterministic()
    {
        var one = TcbFiles.Plain(128);
        var other = TcbFiles.Plain(128);

        TcbMac.Sign(one, MacKey);
        TcbMac.Sign(other, MacKey);

        Assert.Equal(one, other);
    }

    /// <summary>
    /// All four combinations of the two layers round-trip through the shipped reader.
    /// </summary>
    /// <remarks>
    /// They are independent settings, so there are four states and not three: a project can
    /// sign without encrypting, which is the case that has no precedent in the format and the
    /// one a header field laid out for encryption would have made awkward.
    /// </remarks>
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void Every_combination_of_the_two_layers_round_trips(bool encrypt, bool sign)
    {
        var key = new byte[32];
        key[0] = 7;

        var plaintext = TcbFiles.Plain(300);
        var file = (byte[])plaintext.Clone();

        if (encrypt)
            TcbEnvelope.Seal(file, key);

        if (sign)
            TcbMac.Sign(file, MacKey);

        var opened = Tabbit.Binary.TcbTable.Open(
            file, encrypt ? key : null, sign ? MacKey : null).ToArray();

        // The MAC field is the one byte range the reader leaves as it found it: it belongs to
        // the file rather than to the envelope, and a project reading its own files back has
        // no reason to want it erased.
        var expected = (byte[])plaintext.Clone();

        if (sign)
        {
            Array.Copy(
                file, TcbFormat.MacOffset, expected, TcbFormat.MacOffset, TcbFormat.MacSize);
        }

        Assert.Equal(expected, opened);
    }
}
