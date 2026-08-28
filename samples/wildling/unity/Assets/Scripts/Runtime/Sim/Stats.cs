using System;
using System.Collections.Generic;
using System.Linq;
using Wildling.Data;

namespace Wildling.Game
{
    /// <summary>전투에 들어가는 능력치 한 벌이다.</summary>
    public struct StatBlock
    {
        public int Hp;
        public int Attack;
        public int Defense;
        public int Speed;
        public int CritRate;
        public int CritPower;

        public override string ToString()
            => $"hp {Hp} · 공 {Attack} · 방 {Defense} · 속 {Speed}";
    }

    /// <summary>
    /// 능력치를 표에서 계산한다.
    /// </summary>
    /// <remarks>
    /// **세이브에는 레벨과 공명 등급만 남습니다.** 능력치를 저장해 두면 밸런스를 고치고 다시
    /// 변환해도 기존 세이브가 옛 값을 그대로 들고 있게 됩니다 — 이 도구를 쓰는 이유가
    /// 없어지는 자리입니다.
    /// </remarks>
    public static class Stats
    {
        /// <summary>그 단계가 도달할 수 있는 레벨 상한이다.</summary>
        public static int LevelCap(int stage) => stage switch
        {
            1 => GrowthConst.LevelCapStage1,
            2 => GrowthConst.LevelCapStage2,
            _ => GrowthConst.LevelCapStage3,
        };

        /// <summary>그 단계의 액티브 슬롯 수이다.</summary>
        public static int ActiveSlots(int stage)
            => stage <= 1 ? AwakeningConst.ActiveSlotsStage1 : AwakeningConst.ActiveSlotsStage2;

        /// <summary>그 단계의 패시브 슬롯 수이다.</summary>
        public static int PassiveSlots(int stage) => stage switch
        {
            1 => 0,
            2 => AwakeningConst.PassiveSlotsStage2,
            _ => AwakeningConst.PassiveSlotsStage3,
        };

        /// <summary>
        /// 기본치 × 레벨 배수 × 공명 배수이다.
        /// </summary>
        public static StatBlock Compute(MonsterRecord monster, int level, int resonance)
        {
            var b = monster.Base;
            var curve = WildlingData.GrowthCurve.FindByGradeAndLevel(monster.Grade, level);
            var rank = resonance > 0
                ? WildlingData.ResonanceRank.FindByGradeAndRank(monster.Grade, resonance)
                : null;

            int hpF = curve?.HpFactor ?? Numbers.One;
            int atkF = curve?.AttackFactor ?? Numbers.One;
            int defF = curve?.DefenseFactor ?? Numbers.One;
            int resF = rank?.StatFactor ?? Numbers.One;

            // 구간 보너스는 그 레벨에만 붙는 것이 아니라 그 레벨부터 붙습니다.
            int bonus = BonusUpTo(monster.Grade, level);

            return new StatBlock
            {
                Hp = Numbers.Apply(Numbers.Apply(b.Hp, hpF, resF), bonus),
                Attack = Numbers.Apply(Numbers.Apply(b.Attack, atkF, resF), bonus),
                Defense = Numbers.Apply(Numbers.Apply(b.Defense, defF, resF), bonus),
                // 속도와 치명은 곡선을 타지 않습니다. 편성이 순서를 정하게 두기 위한 것입니다.
                Speed = b.Speed,
                CritRate = b.CritRate,
                CritPower = b.CritPower,
            };
        }

        /// <summary>
        /// 그 레벨까지 쌓인 구간 보너스이다.
        /// </summary>
        /// <remarks>
        /// **`bonus_factor` 는 배수가 아니라 증분입니다.** 값이 `2000` 이고 「+20%」라는 뜻입니다
        /// — `hp_factor` 처럼 `min=10000` 제약이 붙어 있지 않은 것이 그 표시입니다. 배수로 읽어
        /// 그대로 곱하면 10레벨마다 능력치가 5분의 1로 줄어듭니다.
        /// </remarks>
        private static int BonusUpTo(Grade grade, int level)
        {
            if (!BonusCache.TryGetValue(grade, out var byLevel))
            {
                byLevel = new Dictionary<int, int>();
                int running = Numbers.One;
                for (int lv = 1; lv <= GrowthConst.LevelCapStage3; lv++)
                {
                    var row = WildlingData.GrowthCurve.FindByGradeAndLevel(grade, lv);
                    if (row != null && row.HasBonusFactor)
                        running = Numbers.Apply(running, Numbers.One + row.BonusFactor);
                    byLevel[lv] = running;
                }
                BonusCache[grade] = byLevel;
            }
            return byLevel.TryGetValue(level, out int value) ? value : Numbers.One;
        }

        private static readonly Dictionary<Grade, Dictionary<int, int>> BonusCache = new();

        /// <summary>표를 다시 읽었으면 계산해 둔 것을 버린다.</summary>
        public static void Forget() => BonusCache.Clear();

        /// <summary>
        /// 재사용 대기가 없는 스킬이 앞에 오게 한다.
        /// </summary>
        /// <remarks>
        /// **슬롯이 전부 대기 중이면 그 턴을 버립니다.** 기획서 9.3 은 「재사용 대기 중인 것은
        /// 건너뜁니다」까지만 정했고 하나도 못 쓰는 경우를 다루지 않았습니다. 대기가 0인 스킬을
        /// 반드시 한 자리 넣으면 그 상태가 생기지 않습니다.
        /// </remarks>
        public static List<MonsterSkillRecord> BasicFirst(
            IEnumerable<MonsterSkillRecord> candidates)
            => candidates
                .Select((row, order) => (row, order))
                .OrderBy(p => (p.row.SkillBySkillId?.Cooldown ?? 9) == 0 ? 0 : 1)
                .ThenBy(p => p.order)
                .Select(p => p.row)
                .ToList();

        /// <summary>레벨 하나를 올리는 데 드는 재화이다.</summary>
        public static GrowthCurveRecord.CostsEntry[] LevelCost(Grade grade, int nextLevel)
            => WildlingData.GrowthCurve.FindByGradeAndLevel(grade, nextLevel)?.Costs
               ?? Array.Empty<GrowthCurveRecord.CostsEntry>();

        /// <summary>공명 등급 하나를 올리는 데 드는 조각이다.</summary>
        public static int ResonanceCost(Grade grade, int nextRank)
            => WildlingData.ResonanceRank.FindByGradeAndRank(grade, nextRank)?.ShardCost ?? 0;
    }
}
