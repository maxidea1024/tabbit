using System;
using System.Text;

using Tabbit.Exporters;

using Xunit;

namespace Tabbit.Tests;

/// <summary>
/// The cipher, against the vectors its specification publishes.
/// </summary>
/// <remarks>
/// A cipher is the one part of this format where being nearly right is indistinguishable
/// from being right until the day a file will not open. Every reader runtime carries its own
/// implementation of these same rounds, so the vector below is not only this code's check -
/// it is the description each of those ports is written against, and the reason a port can
/// be trusted without a round trip through every dataset.
/// </remarks>
public class ChaCha20Tests
{
    /// <summary>
    /// RFC 8439 section 2.4.2: the worked example, key, nonce, counter and all.
    /// </summary>
    /// <remarks>
    /// Counter one rather than zero, which is what the document uses. The format starts its
    /// own files at zero; running the vector as published is worth more than making it match
    /// local practice, because a vector that has been adjusted no longer proves anything
    /// about the document it came from.
    /// </remarks>
    [Fact]
    public void The_cipher_reproduces_the_published_vector()
    {
        var key = new byte[32];
        for (int at = 0; at < key.Length; at++)
            key[at] = (byte)at;

        var nonce = new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x4a, 0x00, 0x00, 0x00, 0x00 };

        var data = Encoding.ASCII.GetBytes(
            "Ladies and Gentlemen of the class of '99: If I could offer you only one tip for "
            + "the future, sunscreen would be it.");

        ChaCha20.Apply(key, nonce, data, counter: 1);

        var expected = Convert.FromHexString(
            "6e2e359a2568f98041ba0728dd0d6981e97e7aec1d4360c20a27afccfd9fae0b"
            + "f91b65c5524733ab8f593dabcd62b3571639d624e65152ab8f530c359f0861d8"
            + "07ca0dbf500d6a6156a38e088a22b65e52bc514d16ccf806818ce91ab7793736"
            + "5af90bbf74a35be6b40b8eedf2785e42874d");

        Assert.Equal(expected, data);
    }

    /// <summary>
    /// Applying the keystream twice returns what went in - which is what makes one routine
    /// serve both directions, in the converter and in every reader.
    /// </summary>
    [Fact]
    public void Applying_the_keystream_twice_returns_the_input()
    {
        var key = new byte[32];
        var nonce = new byte[12];

        for (int at = 0; at < key.Length; at++)
            key[at] = (byte)(at * 7 + 1);

        for (int at = 0; at < nonce.Length; at++)
            nonce[at] = (byte)(at * 3);

        // A length that is not a multiple of the 64-byte block, so the last partial block is
        // exercised as well.
        var original = new byte[1000];
        for (int at = 0; at < original.Length; at++)
            original[at] = (byte)(at % 251);

        var data = (byte[])original.Clone();

        ChaCha20.Apply(key, nonce, data);
        Assert.NotEqual(original, data);

        ChaCha20.Apply(key, nonce, data);
        Assert.Equal(original, data);
    }

    /// <summary>
    /// A file goes into the envelope and comes back out of it unchanged, and the bytes in
    /// between are not the bytes that went in.
    /// </summary>
    [Fact]
    public void A_table_file_survives_the_envelope()
    {
        var key = Convert.FromHexString(
            "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f");

        var plaintext = TcbFiles.Plain(300);
        var file = (byte[])plaintext.Clone();

        TcbEnvelope.Seal(file, key);

        Assert.Equal(TcbFormat.FlagEncrypted, file[TcbFormat.FlagsOffset]);
        Assert.Equal(TcbFormat.CipherChaCha20, file[TcbFormat.CipherOffset]);

        // The body is not readable where it used to be.
        Assert.NotEqual(
            plaintext.AsSpan(TcbFormat.HeaderSize, 50).ToArray(),
            file.AsSpan(TcbFormat.HeaderSize, 50).ToArray());

        Assert.Equal(plaintext, TcbEnvelope.Open(file, key));
    }

    /// <summary>
    /// The signature is at the front of the file, and stays there when the file is sealed.
    /// </summary>
    /// <remarks>
    /// The reason the magic moved out of the ciphertext. It used to be the first four bytes
    /// of the encrypted payload, which left a plain file with no signature at all - so
    /// nothing outside this tool could tell one of these files from any other bytes.
    /// </remarks>
    [Fact]
    public void Every_file_starts_with_the_signature()
    {
        var key = new byte[32];

        var plain = TcbFiles.Plain(64);
        var sealed_ = (byte[])plain.Clone();

        TcbEnvelope.Seal(sealed_, key);

        foreach (var file in new[] { plain, sealed_ })
        {
            Assert.Equal(
                new byte[] { (byte)'T', (byte)'C', (byte)'B', 0 },
                file.AsSpan(0, 4).ToArray());
        }
    }

    /// <summary>
    /// The same input gives the same file, which is what the golden trees rest on.
    /// </summary>
    /// <remarks>
    /// The property the nonce is derived rather than drawn for. It is the reason this format
    /// can be encrypted at all without giving up that two runs over unchanged sheets produce
    /// identical bytes.
    /// </remarks>
    [Fact]
    public void The_same_table_seals_to_the_same_bytes()
    {
        var key = new byte[32];

        var one = TcbFiles.Plain(128);
        var other = TcbFiles.Plain(128);

        TcbEnvelope.Seal(one, key);
        TcbEnvelope.Seal(other, key);

        Assert.Equal(one, other);
    }

    /// <summary>
    /// A different body gets a different nonce, so no keystream is ever used twice under one
    /// key.
    /// </summary>
    [Fact]
    public void A_different_table_gets_a_different_nonce()
    {
        var key = new byte[32];

        var one = TcbFiles.Plain(128);
        var other = TcbFiles.Plain(128);

        other[TcbFormat.HeaderSize + 64] ^= 1;

        TcbEnvelope.Seal(one, key);
        TcbEnvelope.Seal(other, key);

        Assert.NotEqual(
            one.AsSpan(TcbFormat.NonceOffset, TcbFormat.NonceSize).ToArray(),
            other.AsSpan(TcbFormat.NonceOffset, TcbFormat.NonceSize).ToArray());
    }

    /// <summary>
    /// What the converter seals, the shipped reader opens - and gets the same bytes the
    /// unencrypted file would have had.
    /// </summary>
    /// <remarks>
    /// The one assertion that matters about the envelope, because the two sides are separate
    /// implementations: the converter builds the header and the reader takes it apart, and
    /// nothing but this says they agree about where the nonce is, which bytes are covered,
    /// and where the body begins afterwards.
    ///
    /// Byte for byte against the plain file rather than "it parsed", because the reader
    /// returns the envelope's fields to what a plain file holds in them. An off-by-one in the
    /// covered range would decrypt the key check out of the middle of a nonce.
    /// </remarks>
    [Fact]
    public void What_the_converter_seals_the_shipped_reader_opens()
    {
        var key = Convert.FromHexString(
            "1f1e1d1c1b1a191817161514131211100f0e0d0c0b0a09080706050403020100");

        var plaintext = TcbFiles.Plain(512);
        var file = (byte[])plaintext.Clone();

        TcbEnvelope.Seal(file, key);

        var opened = Tabbit.Binary.TcbTable.Open(file, key);

        Assert.Equal(plaintext, opened.ToArray());
    }

    /// <summary>Opening a file twice is opening it once.</summary>
    /// <remarks>
    /// The reader decrypts in place and hands back a window onto the same array, so the
    /// question of what a second call does is a real one. It passes the bytes through: the
    /// fields that said "encrypted" are the ones the first call cleared.
    /// </remarks>
    [Fact]
    public void Opening_an_opened_file_passes_it_through()
    {
        var key = new byte[32];

        var plaintext = TcbFiles.Plain(128);
        var file = (byte[])plaintext.Clone();

        TcbEnvelope.Seal(file, key);

        var once = Tabbit.Binary.TcbTable.Open(file, key).ToArray();
        var twice = Tabbit.Binary.TcbTable.Open(file, key).ToArray();

        Assert.Equal(once, twice);
        Assert.Equal(plaintext, twice);
    }

    /// <summary>An unencrypted file goes through the same call untouched.</summary>
    /// <remarks>
    /// So the call belongs in every load path, whether or not the project uses a key. A
    /// reader that only opened the envelope when it expected one would be a reader that
    /// could not be handed both kinds of file.
    /// </remarks>
    [Fact]
    public void An_unencrypted_file_passes_through_unchanged()
    {
        var plaintext = TcbFiles.Plain(64);

        Assert.Equal(plaintext, Tabbit.Binary.TcbTable.Open(plaintext, null).ToArray());
    }

    /// <summary>An encrypted file with no key says that, rather than failing later.</summary>
    [Fact]
    public void An_encrypted_file_without_a_key_is_refused()
    {
        var key = new byte[32];
        var file = TcbFiles.Plain(64);

        TcbEnvelope.Seal(file, key);

        var failure = Assert.Throws<Tabbit.Binary.TcbException>(
            () => Tabbit.Binary.TcbTable.Open(file, null));

        Assert.Contains("encrypted", failure.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The wrong key is told apart from a damaged file, and says so.
    /// </summary>
    [Fact]
    public void The_wrong_key_is_refused_by_name()
    {
        var written = new byte[32];
        var offered = new byte[32];
        offered[0] = 1;

        var file = TcbFiles.Plain(64);

        TcbEnvelope.Seal(file, written);

        var failure = Assert.Throws<TabbitException>(() => TcbEnvelope.Open(file, offered));

        Assert.Contains("key", failure.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The key check still names a wrong key when the file carries a MAC that verifies.
    /// </summary>
    /// <remarks>
    /// Two keys, two questions. A MAC that passes says the file is the one that was exported;
    /// it says nothing about whether the key about to decrypt it is the one it was sealed
    /// with, and the message a project gets has to be about the key that is actually wrong.
    /// </remarks>
    [Fact]
    public void A_verified_file_still_refuses_the_wrong_encryption_key()
    {
        var written = new byte[32];
        var macKey = new byte[32];
        macKey[0] = 9;

        var offered = new byte[32];
        offered[0] = 1;

        var file = TcbFiles.Plain(64);

        TcbEnvelope.Seal(file, written);
        TcbMac.Sign(file, macKey);

        var failure = Assert.Throws<Tabbit.Binary.TcbException>(
            () => Tabbit.Binary.TcbTable.Open(file, offered, macKey));

        Assert.Contains("key", failure.Message, StringComparison.OrdinalIgnoreCase);
    }
}
