namespace Wildling.Game
{
    /// <summary>
    /// 앞말의 받침에 따라 조사를 고른다.
    /// </summary>
    /// <remarks>
    /// **이름이 표에서 오므로 조사를 손으로 박을 수 없습니다.** 「이끼두꺼비 이(가)」처럼
    /// 두 벌을 나란히 적으면 문장이 읽히지 않고, 하나를 골라 적으면 다른 이름에서 틀립니다.
    ///
    /// 한글 음절은 `0xAC00` 부터 28개 종성 단위로 늘어섭니다. 그래서 `(코드 - 0xAC00) % 28`
    /// 이 0이 아니면 받침이 있습니다.
    /// </remarks>
    public static class Korean
    {
        /// <summary>마지막 글자에 받침이 있는가. 한글이 아니면 없는 것으로 본다.</summary>
        public static bool HasFinal(string word)
        {
            if (string.IsNullOrEmpty(word))
                return false;

            char last = word[^1];
            if (last < 0xAC00 || last > 0xD7A3)
                return false;

            return (last - 0xAC00) % 28 != 0;
        }

        /// <summary>받침이 있으면 <paramref name="withFinal"/>, 없으면 다른 쪽을 붙인다.</summary>
        public static string With(string word, string withFinal, string withoutFinal)
            => word + (HasFinal(word) ? withFinal : withoutFinal);

        public static string Eun(string word) => With(word, "은", "는");
        public static string Ga(string word) => With(word, "이", "가");
        public static string Eul(string word) => With(word, "을", "를");
        public static string Wa(string word) => With(word, "과", "와");
        public static string Euro(string word) => With(word, "으로", "로");

        /// <summary>「~의」처럼 받침을 가리지 않는 조사는 띄어쓰기만 맞춥니다.</summary>
        public static string Ui(string word) => word + "의";
    }
}
