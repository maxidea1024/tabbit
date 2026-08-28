using System;
using System.Globalization;
using System.Text;
using Tabbit.Messages;
using Tabbit.Models;

namespace Tabbit.Cooking;

/// <summary>
/// What a number may be written as in a cell, and what that spelling denotes.
/// </summary>
/// <remarks>
/// The notation is C#'s numeric literal grammar without the suffixes: digit separators,
/// exponents, and `0x` / `0b` literals. The type row already says what type a column is, so
/// `1.5f` would only give an author a second place to say it and a way to disagree with the
/// first.
///
/// Here rather than in <see cref="CookingContext"/> because it is a grammar and not a
/// setting - nothing on it depends on the recipe, on which layout found the cell, or on
/// anything else a run decides. spec/types/number-literals.md.
/// </remarks>
internal static class NumberLiterals
{
    /// <summary>What separates digits of one number, and is removed before it is read.</summary>
    public const char DigitSeparator = '_';

    /// <summary>What groups a decimal's digits by thousands, and is read by the framework.</summary>
    private const char ThousandsSeparator = ',';

    /// <summary>
    /// How far a whole-number reading builds before it gives up and lets the type overflow.
    /// </summary>
    /// <remarks>
    /// A signed 64-bit value is 19 digits, so anything reaching this bound is already out of
    /// range whatever the remaining zeros would have been. The bound exists so `1e2000000000`
    /// is answered rather than allocated.
    /// </remarks>
    private const int DigitLimit = 64;

    /// <summary>
    /// Which types read what is here - the four numeric ones.
    /// </summary>
    /// <remarks>
    /// `float` and `double` included, and not only the integers. A layout that does not
    /// narrow its number columns widens them to `double`, so a rule stopping at the integers
    /// would miss those columns in the configuration that is the default one - and colour
    /// values, which is where radix literals mostly are, sit in exactly them.
    ///
    /// `bitset` is absent because it reads its own literals: what separates it from `bigint`
    /// is which of these spellings it declines. spec/types/bitset.md.
    /// </remarks>
    public static bool ReadsLiterals(Models.ValueType type)
        => type is Models.ValueType.Int32 or Models.ValueType.Int64
            or Models.ValueType.Float or Models.ValueType.Double;

    /// <summary>Whether a type holds whole numbers only.</summary>
    public static bool IsInteger(Models.ValueType type)
        => type is Models.ValueType.Int32 or Models.ValueType.Int64;

    /// <summary>
    /// A cell's number as the text the type's own parser reads.
    /// </summary>
    /// <remarks>
    /// Separators are removed, a radix literal becomes the decimal it denotes, and a
    /// literal with an exponent in an integer column becomes the whole number it names.
    /// Everything the framework parsers already read is handed back untouched, so what they
    /// accept is unchanged for every cell written before any of this existed.
    /// </remarks>
    public static string? OfCell(string? text, Models.ValueType type, Location? location)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        string literal = text!.Trim();

        // Both spellings in one cell. The value is not ambiguous - `1,000_000` is a million
        // either way - but which notation the author meant to be writing is, and a cell
        // holding both is a cell nobody wrote on purpose.
        if (literal.IndexOf(DigitSeparator) >= 0 && literal.IndexOf(ThousandsSeparator) >= 0)
        {
            throw new TabbitException(location,
                Message.Of(CookingMessages.SeparatorsMixed, ("Text", text)));
        }

        int radix = RadixOf(literal);

        if (radix != 0)
            return DecimalOfRadix(literal, type, location);

        string body = WithoutDigitSeparators(literal, text!, location);

        if (!IsInteger(type))
            return body;

        // A decimal point or an exponent in an integer column. Both are read by moving the
        // decimal point through the digits, so no `double` stands between what was written
        // and what is stored.
        return body.IndexOfAny(NotWholeNumberMarks) < 0
            ? body
            : WholeNumberOf(body, text!, type, location);
    }

    /// <summary>What tells a plain run of digits from one that has to be shifted.</summary>
    private static readonly char[] NotWholeNumberMarks = { '.', 'e', 'E' };

    /// <summary>
    /// One component of a composite cell, in the notation its own type reads.
    /// </summary>
    /// <remarks>
    /// A component is a value of its own type, so `(0xFF, 0x8_0, 0x40)` is three integers
    /// written in base 16. The whole-cell colour forms are read before this and never reach
    /// it.
    ///
    /// **Deliberately not <see cref="OfCell"/>.** The whole-number reading is left out, so
    /// an integer component still refuses `1.0`: whether `(1.0, 1.0, 1.0)` is white or three
    /// 255ths is the ambiguity `color32` exists to refuse, and reading `1.0` as `1` here
    /// would answer it on the author's behalf.
    /// </remarks>
    public static string OfComponent(string text, Models.ValueType type, Location? location)
    {
        if (string.IsNullOrEmpty(text) || !ReadsLiterals(type))
            return text;

        string literal = text.Trim();

        return RadixOf(literal) != 0
            ? DecimalOfRadix(literal, type, location)
            : WithoutDigitSeparators(literal, text, location);
    }

    // ------------------------------------------------------------ radix literals

    /// <summary>
    /// The base a `0x` or `0b` literal is written in, or zero when the text is not one.
    /// </summary>
    /// <remarks>
    /// A sign is stepped over rather than judged here, because whether one is allowed is the
    /// type's question: a magnitude may carry a sign and a bit pattern may not.
    /// </remarks>
    public static int RadixOf(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return 0;

        int at = text![0] is '-' or '+' ? 1 : 0;

        if (text.Length < at + 3 || text[at] != '0')
            return 0;

        return text[at + 1] switch
        {
            'x' or 'X' => 16,
            'b' or 'B' => 2,
            _ => 0,
        };
    }

    /// <summary>
    /// A `0x` or `0b` literal as the decimal it denotes, for the type's own parser to read.
    /// </summary>
    /// <remarks>
    /// The base is notation and does not widen the type: the literal becomes the decimal it
    /// denotes and goes through that type's own parser, so `0xFFFFFFFF` in an `int` column is
    /// the overflow it would have been written out.
    /// </remarks>
    private static string DecimalOfRadix(string text, Models.ValueType type, Location? location)
    {
        bool negative = text[0] == '-';
        int at = text[0] is '-' or '+' ? 1 : 0;

        int radix = text[at + 1] is 'x' or 'X' ? 16 : 2;
        ulong magnitude = RadixDigits(text.Substring(at + 2), radix, text, location);

        // Every type reaching here is signed, so the magnitude has to leave room for the
        // sign even when none is written.
        if (magnitude > long.MaxValue)
        {
            throw new TabbitException(location,
                Message.Of(CookingMessages.MagnitudeTooLarge, ("Text", text), ("Type", type)));
        }

        // A float column takes the literal only where it holds the integer exactly. Above
        // the mantissa the value reads back as a neighbouring one, and nothing downstream
        // would say so - the same silent failure the whole-number encoding checks for.
        if (type is Models.ValueType.Float or Models.ValueType.Double)
        {
            ulong exact = type == Models.ValueType.Float ? 1UL << 24 : 1UL << 53;

            if (magnitude > exact)
            {
                throw new TabbitException(location,
                    Message.Of(CookingMessages.FloatLosesExactness,
                        ("Text", text), ("Type", type), ("Exact", exact)));
            }
        }

        return (negative ? "-" : "") + magnitude.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>The digits of a radix literal, refusing whatever the base does not spell.</summary>
    /// <param name="digits">What follows the `0x` or `0b`.</param>
    /// <param name="text">The whole literal, for the messages.</param>
    public static ulong RadixDigits(string digits, int radix, string text, Location? location)
    {
        string plain = WithoutRadixSeparators(digits, text, radix, location);

        int limit = radix == 16 ? 16 : 64;

        if (plain.Length > limit)
        {
            throw new TabbitException(location,
                Message.Of(CookingMessages.RadixTooManyDigits,
                    ("Text", text), ("Digits", plain.Length),
                    ("Radix", radix), ("Limit", limit)));
        }

        foreach (char digit in plain)
        {
            if (!IsRadixDigit(digit, radix))
            {
                throw new TabbitException(location,
                    Message.Of(CookingMessages.RadixBadDigit,
                        ("Text", text), ("Digit", digit), ("Radix", radix)));
            }
        }

        return Convert.ToUInt64(plain, radix);
    }

    private static bool IsRadixDigit(char digit, int radix)
        => radix == 2
            ? digit is '0' or '1'
            : (digit >= '0' && digit <= '9')
                || (digit >= 'a' && digit <= 'f')
                || (digit >= 'A' && digit <= 'F');

    // -------------------------------------------------------- digit separators

    /// <summary>
    /// A decimal literal with its digit separators removed.
    /// </summary>
    /// <remarks>
    /// For `bitset`, which reads its own literals and takes the separator rule from here so
    /// that `1_000` and `0b1010_1010` mean in a flag column what they mean in every other.
    /// </remarks>
    public static string DecimalDigits(string text, Location? location)
        => WithoutDigitSeparators(text, text, location);

    /// <summary>
    /// A decimal literal with its digit separators removed, refusing one that is not
    /// between two digits.
    /// </summary>
    /// <remarks>
    /// `1_000` and `1__0` and `1_0.0_1e1_0` are all spellings of a number. `_1000`, `1000_`,
    /// `1_.0` and `1e_5` are not: in each of those the separator has something other than a
    /// digit on one side, which is where C# stops as well.
    /// </remarks>
    private static string WithoutDigitSeparators(string text, string authored, Location? location)
    {
        if (text.IndexOf(DigitSeparator) < 0)
            return text;

        var built = new StringBuilder(text.Length);

        for (int at = 0; at < text.Length; at++)
        {
            if (text[at] != DigitSeparator)
            {
                built.Append(text[at]);
                continue;
            }

            if (!IsAsciiDigit(NonSeparatorBefore(text, at)) || !IsAsciiDigit(NonSeparatorAfter(text, at)))
                throw MisplacedSeparator(authored, location);
        }

        return built.ToString();
    }

    /// <summary>
    /// A radix literal's digits with its separators removed, likewise.
    /// </summary>
    /// <remarks>
    /// One more position is open here than in a decimal: a separator may sit at the very
    /// front, immediately after the `0x` or `0b`, so `0b_1010_1010` is a spelling of 170.
    /// That is C#'s rule and not an addition to it - the prefix is what the separator has on
    /// its left, and there is no reading of a leading `_` as something else.
    /// </remarks>
    private static string WithoutRadixSeparators(
        string digits, string text, int radix, Location? location)
    {
        if (digits.IndexOf(DigitSeparator) < 0)
            return digits;

        var built = new StringBuilder(digits.Length);

        for (int at = 0; at < digits.Length; at++)
        {
            if (digits[at] != DigitSeparator)
            {
                built.Append(digits[at]);
                continue;
            }

            char before = NonSeparatorBefore(digits, at);
            char after = NonSeparatorAfter(digits, at);

            // `before` is `\0` for a separator run that starts the digits, which is the one
            // the prefix stands to the left of.
            if ((before != '\0' && !IsRadixDigit(before, radix)) || !IsRadixDigit(after, radix))
                throw MisplacedSeparator(text, location);
        }

        // `0x_` and `0b__`. Every character was a separator, so the literal names no value.
        if (built.Length == 0)
            throw MisplacedSeparator(text, location);

        return built.ToString();
    }

    private static TabbitException MisplacedSeparator(string text, Location? location)
        => new TabbitException(location,
            Message.Of(CookingMessages.DigitSeparatorMisplaced,
                ("Text", text), ("Separator", DigitSeparator)));

    /// <summary>The character left of `at`, separators stepped over. `\0` if there is none.</summary>
    private static char NonSeparatorBefore(string text, int at)
    {
        int before = at - 1;

        while (before >= 0 && text[before] == DigitSeparator)
            before--;

        return before >= 0 ? text[before] : '\0';
    }

    /// <summary>The character right of `at`, separators stepped over. `\0` if there is none.</summary>
    private static char NonSeparatorAfter(string text, int at)
    {
        int after = at + 1;

        while (after < text.Length && text[after] == DigitSeparator)
            after++;

        return after < text.Length ? text[after] : '\0';
    }

    private static bool IsAsciiDigit(char character) => character >= '0' && character <= '9';

    // ------------------------------------------------------------ whole numbers

    /// <summary>
    /// A literal with a decimal point or an exponent as the whole number it names, or a
    /// refusal when it does not name one.
    /// </summary>
    /// <remarks>
    /// This is the one place the notation departs from C#, where `int x = 1e3;` is an error
    /// and the fix is a cast. Two things say a sheet should not work that way: a spreadsheet
    /// writes large numbers as `1E+15` on its own, so an integer column refusing that would
    /// be refusing a cell nobody typed; and a type row has nowhere to write the cast that
    /// says "the loss is intended", so a loss is answered with a message instead.
    ///
    /// **The digits are shifted, not converted.** `1e3` is the digits of `1` with the point
    /// moved three places, so nothing here goes through a `double` and no value arrives at
    /// its neighbour. A digit other than zero falling off the end is what "not a whole
    /// number" means, and it is refused at exactly that point.
    ///
    /// Text this does not recognize is handed back unchanged, so the type's own parser
    /// reports it - its message names the character, which is more than this could say.
    /// </remarks>
    private static string WholeNumberOf(
        string text, string authored, Models.ValueType type, Location? location)
    {
        int at = 0;
        bool negative = false;

        if (at < text.Length && text[at] is '-' or '+')
        {
            negative = text[at] == '-';
            at++;
        }

        var digits = new StringBuilder(text.Length);

        while (at < text.Length && (IsAsciiDigit(text[at]) || text[at] == ThousandsSeparator))
        {
            if (text[at] != ThousandsSeparator)
                digits.Append(text[at]);

            at++;
        }

        // How many of the digits gathered so far sit left of the point.
        int whole = digits.Length;

        if (at < text.Length && text[at] == '.')
        {
            at++;

            while (at < text.Length && IsAsciiDigit(text[at]))
            {
                digits.Append(text[at]);
                at++;
            }
        }

        int exponent = 0;

        if (at < text.Length && text[at] is 'e' or 'E')
        {
            at++;

            bool negativeExponent = at < text.Length && text[at] == '-';

            if (at < text.Length && text[at] is '-' or '+')
                at++;

            int from = at;

            while (at < text.Length && IsAsciiDigit(text[at]))
                at++;

            if (at == from)
                return text;

            // An exponent too large for an `int` is one no integer type holds either, so
            // the bound stands in for it and the overflow is reported below all the same.
            if (!int.TryParse(
                    text.Substring(from, at - from), NumberStyles.None,
                    CultureInfo.InvariantCulture, out exponent))
            {
                exponent = DigitLimit + 1;
            }

            if (negativeExponent)
                exponent = -exponent;
        }

        if (at != text.Length || digits.Length == 0)
            return text;

        int shift = exponent - (digits.Length - whole);

        return WithPointMoved(digits.ToString(), shift, negative, authored, type, location);
    }

    /// <summary>
    /// The digits with the decimal point moved `shift` places right, as an integer.
    /// </summary>
    private static string WithPointMoved(
        string digits, int shift, bool negative, string authored,
        Models.ValueType type, Location? location)
    {
        string magnitude;

        if (shift >= 0)
        {
            // Past the bound the value overflows whatever the remaining zeros are, so the
            // string stops there and the type's parser says so.
            magnitude = digits + new string('0', Math.Min(shift, DigitLimit));
        }
        else
        {
            int dropped = -shift;
            int kept = digits.Length - dropped;

            for (int at = Math.Max(kept, 0); at < digits.Length; at++)
            {
                if (digits[at] != '0')
                {
                    throw new TabbitException(location,
                        Message.Of(CookingMessages.NotAWholeNumber,
                            ("Text", authored), ("Type", type)));
                }
            }

            magnitude = kept > 0 ? digits.Substring(0, kept) : "0";
        }

        magnitude = magnitude.TrimStart('0');

        if (magnitude.Length == 0)
            return "0";

        return negative ? "-" + magnitude : magnitude;
    }
}
