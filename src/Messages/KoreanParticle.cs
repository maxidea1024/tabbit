using System;

namespace Tabbit.Messages;

/// <summary>
/// Which of a Korean particle's two forms follows a value.
/// </summary>
/// <remarks>
/// Korean pairs its particles - 은/는, 이/가, 을/를, 와/과, 으로/로 - and which one is written
/// depends on the sound the word before it ends in. A value dropped into a sentence therefore
/// decides the grammar around it, which no other language in this catalog does: the sentence
/// cannot be written until the value is known.
///
/// So the catalog writes the pair and this picks. `{Type:은}` in a Korean entry means "the
/// 은/는 pair, whichever fits `{Type}`" - and the same entry in the other four catalogs has no
/// such notation, so they pay nothing for it.
///
/// **The values here are mostly not Korean.** They are type names, column names, file paths -
/// `int`, `MaxHitPoints`, `Asia/Seoul`. A rule that only looked at Hangul syllables would get
/// every one of them wrong, so the letters and digits are spelled out below: `int` ends in a
/// consonant sound and takes 은, `float` ends in a vowel sound and takes 는. Nobody would
/// guess `0` takes 은 and `2` takes 는 from first principles; they are read aloud as 영 and
/// 이, and that is what decides it.
/// </remarks>
public static class KoreanParticle
{
    /// <summary>The pairs a catalog entry may ask for, first form after a consonant.</summary>
    private static readonly (string Closed, string Open)[] Pairs =
    [
        ("은", "는"),
        ("이", "가"),
        ("을", "를"),
        ("과", "와"),
        ("으로", "로"),
        ("이라", "라"),
        ("이어야", "여야"),
        ("이나", "나"),
        ("이든", "든"),
        ("이며", "며"),
        ("아", "야"),
    ];

    /// <summary>
    /// Whether <paramref name="written"/> asks for a particle, and which pair.
    /// </summary>
    public static bool IsParticle(string written, out string closed, out string open)
    {
        foreach (var (first, second) in Pairs)
        {
            if (string.Equals(written, first, StringComparison.Ordinal)
                || string.Equals(written, second, StringComparison.Ordinal))
            {
                closed = first;
                open = second;
                return true;
            }
        }

        closed = "";
        open = "";
        return false;
    }

    /// <summary>
    /// The form of the pair that follows <paramref name="value"/>.
    /// </summary>
    /// <remarks>
    /// An empty value, or one ending in something with no sound of its own - a bracket, a
    /// quote, punctuation - is answered as if it ended in a consonant. That is the form that
    /// reads least wrongly when the guess is wrong, and a value ending in a bracket is a value
    /// whose last spoken sound this cannot see anyway.
    /// </remarks>
    public static string For(string value, string closed, string open)
        => EndsInConsonantSound(value) ? closed : open;

    private static bool EndsInConsonantSound(string value)
    {
        char last = LastSpoken(value);

        if (last == '\0')
            return true;

        // A Hangul syllable carries its final consonant in the code point: the block is laid
        // out as (initial, medial, final), so the remainder says whether there is a final.
        if (last >= '가' && last <= '힣')
            return (last - '가') % 28 != 0;

        if (char.IsDigit(last))
            return DigitEndsInConsonant(last);

        if (last is >= 'a' and <= 'z' or >= 'A' and <= 'Z')
            return LetterEndsInConsonant(char.ToLowerInvariant(last));

        return true;
    }

    /// <summary>The last character with a sound, skipping what is only punctuation.</summary>
    private static char LastSpoken(string value)
    {
        for (int at = value.Length - 1; at >= 0; at--)
        {
            char here = value[at];

            if (char.IsLetterOrDigit(here))
                return here;
        }

        return '\0';
    }

    /// <summary>
    /// Whether a digit read aloud in Korean ends in a consonant.
    /// </summary>
    /// <remarks>
    /// 영 일 이 삼 사 오 육 칠 팔 구 - so 0, 1, 3, 6, 7, 8 close and 2, 4, 5, 9 do not.
    /// </remarks>
    private static bool DigitEndsInConsonant(char digit)
        => digit is '0' or '1' or '3' or '6' or '7' or '8';

    /// <summary>
    /// Whether a Latin letter read aloud in Korean ends in a consonant.
    /// </summary>
    /// <remarks>
    /// The names are 에이 비 시 디 이 에프 지 에이치 아이 제이 케이 엘 엠 엔 오 피 큐 알 에스
    /// 티 유 브이 더블유 엑스 와이 제트. Only 엘 엠 엔 알 - l, m, n, r - end on a consonant;
    /// every other name ends in a vowel, including the ones that look like they should not.
    /// 에스 and 엑스 end in ㅡ, and 티 and 제트 do too.
    ///
    /// A value ending in a letter is usually a word rather than a spelled-out abbreviation -
    /// `int`, `float`, `MaxHitPoints` - and the same rule holds, because a Korean reader says
    /// 인트 and 플로트 and both end on ㅡ. That is worth stating because it is easy to get
    /// backwards: `int` looks closed and is not, while `bool` is 불 and is.
    /// </remarks>
    private static bool LetterEndsInConsonant(char letter)
        => letter is 'l' or 'm' or 'n' or 'r';
}
