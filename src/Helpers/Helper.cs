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
        MD5 md5 = MD5.Create();

        for (int i = 0; i < filenames.Length; i++)
        {
            var filename = filenames[i];

            byte[] data = File.ReadAllBytes(filename);

            if (i == filenames.Length - 1)
                md5.TransformFinalBlock(data, 0, data.Length);
            else
                md5.TransformBlock(data, 0, data.Length, data, 0);
        }

        if (md5.Hash is null)
            return "";

        return BitConverter.ToString(md5.Hash).Replace("-", "").ToLowerInvariant();
    }
}
