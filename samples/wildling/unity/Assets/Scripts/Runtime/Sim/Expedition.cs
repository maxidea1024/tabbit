using System;
using System.Collections.Generic;
using System.Linq;
using Wildling.Data;

namespace Wildling.Game
{
    /// <summary>탐사 한 번의 결과이다.</summary>
    public sealed class ExpeditionResult
    {
        public string RegionId;
        public int Seconds;
        public int CappedSeconds;
        public bool HitCap;
        public readonly List<Grant> Grants = new();
        public readonly List<string> Discovered = new();
    }

    /// <summary>
    /// 탐사와 방치 정산이다.
    /// </summary>
    /// <remarks>
    /// **시간대가 뒤로 갈수록 산출이 줄어듭니다.** `RegionYield` 가 `hour_band` 별로 값을
    /// 따로 들고 있으므로 계수를 코드에 두지 않습니다 — 체감 곡선을 고치는 것이 시트 수정으로
    /// 끝나는 자리입니다.
    ///
    /// **`EncounterTable` 은 원래 서버가 굴리는 표입니다**(`side=s`). 이 게임은 단독 실행이라
    /// 클라이언트가 굴립니다. 서버가 있는 프로젝트라면 이 함수가 서버에 있게 됩니다.
    /// </remarks>
    public static class Expedition
    {
        public static ExpeditionResult Settle(GameState state, string regionId,
                                              long startedUtc, long nowUtc, Rng rng)
        {
            var result = new ExpeditionResult { RegionId = regionId };

            int elapsed = (int)Math.Max(0, nowUtc - startedUtc);
            int cap = IdleConst.CapHours * 3600;
            result.Seconds = elapsed;
            result.CappedSeconds = Math.Min(elapsed, cap);
            result.HitCap = elapsed >= cap;

            if (result.CappedSeconds <= 0)
                return result;

            var grants = new List<Grant>();
            int remaining = result.CappedSeconds;

            for (int band = 0; band < IdleConst.CapHours && remaining > 0; band++)
            {
                int slice = Math.Min(3600, remaining);
                remaining -= slice;

                var yield = WildlingData.RegionYield.FindByRegionIdAndHourBand(regionId, band);
                if (yield is null)
                    continue;

                // 그 시간대를 다 채우지 못했으면 그만큼만 받습니다.
                int part = (int)((long)slice * Numbers.One / 3600);

                int gold = Numbers.Apply(yield.GoldPerHour, part);
                int food = Numbers.Apply(yield.FoodPerHour, part);
                if (gold > 0)
                    grants.Add(new Grant { Kind = GrantKind.Currency, Id = "gold", Amount = gold });
                if (food > 0)
                    grants.Add(new Grant { Kind = GrantKind.Currency, Id = "food", Amount = food });

                // 재료는 시간대마다 한 번 굴립니다.
                grants.AddRange(Rewards.Roll(yield.RewardGroupId, rng));
            }

            // ---------------------------------------------------------- 발견

            int hours = Math.Max(1, result.CappedSeconds / 3600);
            var encounter = WildlingData.EncounterTable.Records
                .FirstOrDefault(e => e.RegionId == regionId);

            if (encounter != null)
            {
                bool hidden = !encounter.HasRequirementGroupId
                              || Requirements.Met(encounter.RequirementGroupId, state, null);

                var pool = encounter.Entries
                    .Where(e => e.EncounterSlot == EncounterSlot.Normal
                                || (e.EncounterSlot == EncounterSlot.Hidden && hidden))
                    .ToList();

                var rare = encounter.Entries
                    .Where(e => e.EncounterSlot == EncounterSlot.Rare)
                    .ToList();

                for (int i = 0; i < hours; i++)
                    DrawOne(state, pool, grants, result, rng);

                // 희귀 슬롯은 탐사 1회당 최대 1회입니다.
                if (rare.Count > 0 && result.CappedSeconds >= 4 * 3600)
                    DrawOne(state, rare, grants, result, rng);
            }

            result.Grants.AddRange(Rewards.Merge(grants));
            return result;
        }

        /// <summary>
        /// 출현 목록에서 하나를 뽑는다.
        /// </summary>
        /// <remarks>
        /// **미기록 종에 가중치 보정을 적용합니다.** `CollectionConst.UnrecordedBoost` 이고,
        /// 마지막 한 종이 나오지 않아 진행이 멈추는 것을 억제하기 위한 것입니다.
        /// </remarks>
        private static void DrawOne(GameState state,
                                    List<EncounterTableTable.Record.EntriesEntry> pool,
                                    List<Grant> grants, ExpeditionResult result, Rng rng)
        {
            if (pool.Count == 0)
                return;

            var weights = pool.Select(e =>
            {
                int weight = e.Weight;
                if (state.CodexState(e.MonsterId) < CodexState.Recorded)
                    weight = Numbers.Apply(weight, CollectionConst.UnrecordedBoost);
                return weight;
            }).ToArray();

            int index = rng.Weighted(weights);
            if (index < 0)
                return;

            string monsterId = pool[index].MonsterId;
            grants.Add(new Grant { Kind = GrantKind.Monster, Id = monsterId, Amount = 1 });

            if (state.CodexState(monsterId) < CodexState.Recorded)
                result.Discovered.Add(monsterId);
        }

        /// <summary>남은 시간을 「3시간 12분」처럼 적는다.</summary>
        public static string Elapsed(int seconds)
        {
            int h = seconds / 3600;
            int m = seconds % 3600 / 60;
            if (h > 0)
                return m > 0 ? $"{h}시간 {m}분" : $"{h}시간";
            return m > 0 ? $"{m}분" : $"{seconds}초";
        }
    }
}
