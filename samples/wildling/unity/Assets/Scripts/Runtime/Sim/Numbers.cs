using System;

namespace Wildling.Game
{
    /// <summary>
    /// 만분율 계산과 결정적 난수이다.
    /// </summary>
    /// <remarks>
    /// **시트의 배수는 전부 만분율 정수입니다.** `13500` 이 1.35배이고, `10000` 이 그대로입니다.
    /// 부동소수로 옮기면 같은 세이브가 기계마다 다른 값을 낼 수 있으므로 정수로 유지하고
    /// 마지막에 한 번 나눕니다.
    /// </remarks>
    public static class Numbers
    {
        public const int One = 10000;

        /// <summary>`value` 에 만분율 `factor` 를 적용한다.</summary>
        public static int Apply(int value, int factor)
            => (int)((long)value * factor / One);

        /// <summary>`value` 에 만분율 배수를 차례로 적용한다.</summary>
        public static int Apply(int value, int a, int b)
            => (int)((long)value * a / One * b / One);

        public static int Clamp(int value, int low, int high)
            => value < low ? low : (value > high ? high : value);

        /// <summary>만분율을 「1.35배」처럼 읽는 문자열로 만든다.</summary>
        public static string AsMultiplier(int factor)
            => (factor / (double)One).ToString("0.##") + "배";

        /// <summary>만분율을 백분율 문자열로 만든다.</summary>
        public static string AsPercent(int factor)
            => (factor / 100.0).ToString("0.#") + "%";

        /// <summary>큰 수를 짧게 적는다. 재화 표시에 쓴다.</summary>
        public static string Short(long value)
        {
            if (value < 10000) return value.ToString();
            if (value < 100000000) return (value / 10000.0).ToString("0.#") + "만";
            return (value / 100000000.0).ToString("0.##") + "억";
        }
    }

    /// <summary>
    /// 씨앗에서 값을 내는 난수기이다.
    /// </summary>
    /// <remarks>
    /// `System.Random` 을 쓰지 않는 것은 **같은 씨앗이 어느 판에서나 같은 값을 내야 하기
    /// 때문**입니다. 자동 플레이 검사가 매번 같은 경로를 밟아야 실패를 재현할 수 있습니다.
    /// </remarks>
    public sealed class Rng
    {
        private uint _state;

        public Rng(int seed) => _state = (uint)(seed | 1);

        public Rng(string seed)
        {
            uint value = 2166136261;
            foreach (char c in seed)
                value = (value ^ c) * 16777619;
            _state = value | 1;
        }

        public uint Next()
        {
            uint x = _state;
            x ^= x << 13;
            x ^= x >> 17;
            x ^= x << 5;
            return _state = x;
        }

        /// <summary>0 이상 <paramref name="bound"/> 미만이다.</summary>
        public int Below(int bound) => bound <= 0 ? 0 : (int)(Next() % (uint)bound);

        /// <summary>만분율 확률로 참이다.</summary>
        public bool Chance(int rate) => rate > 0 && Below(Numbers.One) < rate;

        /// <summary>가중치 목록에서 하나를 고른다. 전부 0이면 -1 이다.</summary>
        public int Weighted(int[] weights)
        {
            long total = 0;
            foreach (int w in weights)
                total += Math.Max(0, w);
            if (total <= 0)
                return -1;

            long roll = (long)(Next() % (uint)Math.Min(total, uint.MaxValue));
            for (int i = 0; i < weights.Length; i++)
            {
                roll -= Math.Max(0, weights[i]);
                if (roll < 0)
                    return i;
            }
            return weights.Length - 1;
        }
    }
}
