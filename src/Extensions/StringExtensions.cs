using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Tabbit.Extensions;

public enum NameCase
{
    None,
    Camel,
    Pascal,
    Kebab,
    Snake,
    Train,
    Sentence
}

public static class StringExtensions
{
    public static string SafeTrim(this string s)
    {
        return s is not null ? s.Trim() : "";
    }

    //https://stackoverflow.com/questions/1904252/is-there-a-method-in-c-sharp-to-check-if-a-string-is-a-valid-identifier
    public static bool IsValidIdentifier(this string identifier)
    {
        if (String.IsNullOrEmpty(identifier))
            return false;

        // definition of a valid C# identifier: http://msdn.microsoft.com/en-us/library/aa664670(v=vs.71).aspx
        const string formattingCharacter = @"\p{Cf}";
        const string connectingCharacter = @"\p{Pc}";
        const string decimalDigitCharacter = @"\p{Nd}";
        const string combiningCharacter = @"\p{Mn}|\p{Mc}";
        const string letterCharacter = @"\p{Lu}|\p{Ll}|\p{Lt}|\p{Lm}|\p{Lo}|\p{Nl}";
        const string identifierPartCharacter = letterCharacter + "|" +
                                               decimalDigitCharacter + "|" +
                                               connectingCharacter + "|" +
                                               combiningCharacter + "|" +
                                               formattingCharacter;
        const string identifierPartCharacters = "(" + identifierPartCharacter + ")+";
        const string identifierStartCharacter = "(" + letterCharacter + "|_)";
        const string identifierOrKeyword = identifierStartCharacter + "(" +
                                           identifierPartCharacters + ")*";
        var validIdentifierRegex = new Regex("^" + identifierOrKeyword + "$", RegexOptions.Compiled);
        var normalizedIdentifier = identifier.Normalize();

        // Check that the identifier match the validIdentifer regex.
        return validIdentifierRegex.IsMatch(normalizedIdentifier);
    }


    #region Case conversion

    public static string ToCase(this string source, NameCase targetNameCase)
    {
        switch (targetNameCase)
        {
            case NameCase.Camel:
                return ToCamelCase(source);
            case NameCase.Pascal:
                return ToPascalCase(source);
            case NameCase.Kebab:
                return ToKebabCase(source);
            case NameCase.Snake:
                return ToSnakeCase(source);
            case NameCase.Train:
                return ToTrainCase(source);
            case NameCase.Sentence:
                return ToSentenceCase(source);
        }

        return source;
    }

    [return: NotNullIfNotNull(nameof(source))]
    public static string? ToCamelCase(this string? source)
    {
        if (source is null)
            return null;

        return SymbolsPipe(
            source,
            '\0',
            (s, disableFrontDelimeter) =>
            {
                if (disableFrontDelimeter)
                    return [char.ToLowerInvariant(s)];

                return [char.ToUpperInvariant(s)];
            });
    }

    [return: NotNullIfNotNull(nameof(source))]
    public static string? ToPascalCase(this string? source)
    {
        if (source is null)
            return null;

        return SymbolsPipe(
            source,
            '\0',
            (s, i) => [char.ToUpperInvariant(s)]);
    }

    [return: NotNullIfNotNull(nameof(source))]
    public static string? ToKebabCase(this string? source)
    {
        if (source is null)
            return null;

        return SymbolsPipe(
            source,
            '-',
            (s, disableFrontDelimeter) =>
            {
                if (disableFrontDelimeter)
                    return [char.ToLowerInvariant(s)];

                return ['-', char.ToLowerInvariant(s)];
            },
            Lower);
    }

    [return: NotNullIfNotNull(nameof(source))]
    public static string? ToSnakeCase(this string? source)
    {
        if (source is null)
            return null;

        return SymbolsPipe(
            source,
            '_',
            (s, disableFrontDelimeter) =>
            {
                if (disableFrontDelimeter)
                    return [char.ToLowerInvariant(s)];

                return ['_', char.ToLowerInvariant(s)];
            },
            Lower);
    }

    [return: NotNullIfNotNull(nameof(source))]
    public static string? ToTrainCase(this string? source)
    {
        if (source is null)
            return null;

        return SymbolsPipe(
            source,
            '-',
            (s, disableFrontDelimeter) =>
            {
                if (disableFrontDelimeter)
                    return [char.ToUpperInvariant(s)];

                return ['-', char.ToUpperInvariant(s)];
            },
            Keep);
    }

    public static string ToSentenceCase(this string source)
    {
        if (string.IsNullOrEmpty(source))
            return source;

        string result = "";

        bool needToUpper = false;
        for (int i = 0; i < source.Length; i++)
        {
            if (i == 0)
            {
                result += char.ToUpper(source[i]);
            }
            else if (source[i] == '_')
            {
                needToUpper = true;
                result += " ";
            }
            else
            {
                if (needToUpper)
                {
                    needToUpper = false;
                    result += char.ToUpper(source[i]);
                }
                else
                {
                    result += source[i];
                }
            }
        }

        return result;
    }


    private static readonly char[] Delimeters = [ ' ', '-', '_' ];

    /// <summary>
    /// What happens to a character that continues the word it is in, rather than opening
    /// one.
    /// </summary>
    /// <remarks>
    /// Only the first character of each word reaches
    /// <c>newWordSymbolHandler</c>; the rest used to be copied through untouched, which is
    /// right when the input is Pascal case because everything after the first letter is
    /// already lowercase. It stops being right inside an acronym: `HP` gave `hP`, because
    /// only the `H` was ever lowered. The casing forms that flatten a name say so here.
    /// </remarks>
    private static char Keep(char symbol) => symbol;
    private static char Lower(char symbol) => char.ToLowerInvariant(symbol);
    private static char Upper(char symbol) => char.ToUpperInvariant(symbol);

    private static string SymbolsPipe(
        string source, char mainDelimeter, Func<char, bool, char[]> newWordSymbolHandler,
        Func<char, char>? continuationHandler = null)
    {
        if (string.IsNullOrEmpty(source))
            return source;

        // No special case for an all-caps name. Pascal-casing already preserves a
        // run of capitals - `HP` stays `HP` - because every uppercase character is
        // treated as starting a word.

        // Leading and trailing underscores are kept: `_reserved` is a name somebody
        // chose, and the casing forms have no business removing it.
        string headUnderscores = "";
        string tailUnderscores = "";

        int headUnderscoreCount = 0;
        int tailUnderscoreCount = 0;

        for (int i = 0; i < source.Length; i++)
        {
            if (source[i] == '_')
                headUnderscoreCount++;
            else
                break;
        }

        if (headUnderscoreCount > 0)
        {
            headUnderscores = source.Substring(0, headUnderscoreCount);
            source = source.Substring(headUnderscoreCount);

        }

        for (int i = source.Length-1; i >= 0; i--)
        {
            if (source[i] == '_')
                tailUnderscoreCount++;
            else
                break;
        }

        if (tailUnderscoreCount > 0)
        {
            tailUnderscores = source.Substring(source.Length - tailUnderscoreCount);
            source = source.Substring(0, source.Length - tailUnderscoreCount);
        }


        var builder = new StringBuilder();

        bool nextSymbolStartsNewWord = true;
        bool disableFrontDelimeter = true;
        for (var i = 0; i < source.Length; i++)
        {
            var symbol = source[i];
            if (Delimeters.Contains(symbol))
            {
                if (symbol == mainDelimeter)
                {
                    builder.Append(symbol);
                    disableFrontDelimeter = true;
                }

                nextSymbolStartsNewWord = true;
            }
            else if (!char.IsLetterOrDigit(symbol))
            {
                builder.Append(symbol);
                disableFrontDelimeter = true;
                nextSymbolStartsNewWord = true;
            }
            else
            {
                if (nextSymbolStartsNewWord || StartsNewWord(source, i))
                {
                    builder.Append(newWordSymbolHandler(symbol, disableFrontDelimeter));
                    disableFrontDelimeter = false;
                    nextSymbolStartsNewWord = false;
                }
                else
                {
                    builder.Append(continuationHandler is null ? symbol : continuationHandler(symbol));
                }
            }
        }

        return headUnderscores + builder.ToString() + tailUnderscores;
    }

    /// <summary>
    /// Whether the character at <paramref name="index"/> begins a word.
    /// </summary>
    /// <remarks>
    /// A run of capitals is one word, not one word per letter. Every uppercase character
    /// used to start one, which is invisible in Pascal case - `SFXCategoryType` comes back
    /// out as itself either way - and wrong everywhere else: the snake-case languages were
    /// given `enum_s_f_x_category_type.py`, and fields written `ATK_Growth` or `Name_KR` in
    /// the sheet reached Python as `a_t_k_growth` and `name_k_r`.
    ///
    /// So a capital opens a word when the character before it is not one, and a capital
    /// inside a run opens one only when the character after it is lowercase - that being
    /// where the acronym stops and the next word starts. `SFXCategoryType` is SFX, Category
    /// and Type; `HTTPServer` is HTTP and Server; `HP` and `KR` stay whole.
    /// </remarks>
    private static bool StartsNewWord(string source, int index)
    {
        if (!char.IsUpper(source[index]))
            return false;

        // The caller has already said yes for the first character, and for anything
        // following a delimiter.
        if (index == 0)
            return true;

        if (!char.IsUpper(source[index - 1]))
            return true;

        return index + 1 < source.Length && char.IsLower(source[index + 1]);
    }

    #endregion
}
