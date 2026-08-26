using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Tabbit.Helpers;

/// <summary>
/// Small shared utilities: name-splitting for serial fields, and content hashing for
/// the manifest.
/// </summary>
public static class Helper
{
    /// <summary>
    /// The name with every digit removed, which is the stem serial-field columns are
    /// grouped by: `Text1` and `Text2` both reduce to `Text`.
    ///
    /// Digits are dropped wherever they appear rather than only at the end, so a name
    /// carrying two separate runs of them reduces to something ambiguous. That is
    /// tolerable because GetSerialFieldPattern has already rejected such names.
    /// </summary>
    public static string StripNumber(string str)
    {
        string result = "";
        for (int i = 0; i < str.Length; i++)
        {
            if (!char.IsDigit(str[i]))
                result += str[i];
        }

        return result;
    }

    /// <summary>
    /// The digits of a name, joined, which is a serial-field column's sequence number.
    ///
    /// The counterpart to <see cref="StripNumber"/>, and subject to the same
    /// restriction: only meaningful for a name with exactly one run of digits.
    /// </summary>
    public static string ExtractNumber(string str)
    {
        string result = "";
        for (int i = 0; i < str.Length; i++)
        {
            if (char.IsDigit(str[i]))
                result += str[i];
        }

        return result;
    }

    /// <summary>
    /// MD5 of a byte array, as lower-case hex.
    ///
    /// MD5 throughout this file, and deliberately: these hashes tell a patcher whether
    /// a file changed, which is a content-identity question rather than a security
    /// one. Nothing here defends against a chosen-prefix attack.
    /// </summary>
    public static string CalculateMD5HashFromBytes(byte[] data)
    {
        using var md5Provider = MD5.Create();
        var hash = md5Provider.ComputeHash(data);
        if (hash is null)
            return "";
        return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
    }

    /// <summary>MD5 of a string's UTF-8 bytes, as lower-case hex.</summary>
    public static string CalculateMD5HashFromString(string str)
    {
        using var md5Provider = MD5.Create();
        var hash = md5Provider.ComputeHash(Encoding.UTF8.GetBytes(str));
        if (hash is null)
            return "";
        return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
    }

    /// <summary>MD5 of a file's contents, as lower-case hex.</summary>
    public static string CalculateMD5HashFromFile(string filename)
    {
        byte[] data = File.ReadAllBytes(filename);
        return CalculateMD5HashFromBytes(data);
    }

    /// <summary>
    /// One MD5 over several files' contents, in the order given.
    ///
    /// Order-sensitive, so callers that want a stable result must sort first. Used for
    /// the manifest's master hash, which answers "did any of this change" in one
    /// comparison.
    /// </summary>
    public static string CalculateMD5HashFromFiles(string[] filenames)
    {
        // Streamed rather than read whole. The digest is the same either way - it is one
        // hash over the files' bytes in the order given - but reading a file into an array
        // first meant the manifest's master hash allocated the whole of the output it was
        // summarising: on the sample project's `json` target, 288 MB of arrays for a value
        // sixteen bytes long. spec/ops/conversion-time.md section 4.
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.MD5);

        byte[] buffer = new byte[64 * 1024];

        foreach (var filename in filenames)
        {
            using var stream = new FileStream(
                filename, FileMode.Open, FileAccess.Read, FileShare.ReadWrite,
                buffer.Length, FileOptions.SequentialScan);

            int read;
            while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
                digest.AppendData(buffer, 0, read);
        }

        return Convert.ToHexString(digest.GetHashAndReset()).ToLowerInvariant();
    }
}
