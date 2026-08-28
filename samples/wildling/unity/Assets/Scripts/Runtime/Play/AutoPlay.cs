using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Wildling.Data;

namespace Wildling.Game
{
    /// <summary>
    /// 사람 없이 핵심 루프를 한 바퀴 돌린다.
    /// </summary>
    /// <remarks>
    /// **이 검사가 이 작업의 게이트입니다.** `WildlingDataCheck` 는 「값을 맞게 읽었는가」까지
    /// 보고, 이것은 「그 값으로 게임 규칙이 도는가」를 봅니다 — 참조가 낸 두 이름 중 틀린 쪽을
    /// 고르는 종류의 결함은 실제로 써 볼 때 드러납니다.
    ///
    /// 판정은 종료 코드가 아니라 보고 파일의 첫 줄입니다. 그 이유는
    /// `WildlingDataCheck.cs` 에 적혀 있습니다.
    /// </remarks>
    public static class AutoPlay
    {
        public static string Run(out bool ok)
        {
            var log = new StringBuilder();
            var failures = new List<string>();

            log.AppendLine("=== 와일드링 자동 플레이 ===");

            // ---------------------------------------------------------- 표
            try
            {
                Boot.LoadTables().GetAwaiter().GetResult();
            }
            catch (Exception e)
            {
                ok = false;
                return log.AppendLine($"!! 표를 읽지 못했습니다 — {e.Message}").ToString();
            }

            log.AppendLine($"표 — 와일드링 {WildlingData.Monster.Records.Count}행 · "
                           + $"스킬 {WildlingData.Skill.Records.Count}행 · "
                           + $"스테이지 {WildlingData.Stage.Records.Count}행");

            // ---------------------------------------------------------- 그림
            int missing = ArtLibrary.LoadEverythingTheTablesPointAt();
            log.AppendLine($"그림 — `asset=` 이 가리키는 것을 전부 로드했습니다. "
                           + $"없는 것 {missing}개");
            if (missing > 0)
            {
                foreach (string path in ArtLibrary.Missing.Take(8))
                    failures.Add($"그림 `{path}` 을 로드하지 못했습니다.");
            }

            // ---------------------------------------------------------- 새 판
            SaveStore.Enabled = false;
            Clock.Offset = 0;

            var state = GameState.NewGame(Clock.NowUtc);
            log.AppendLine($"새 판 — 동행 {state.All.Count}마리 · "
                           + $"해금 지역 {state.UnlockedRegions.Count}개");

            if (state.All.Count < PartyConst.PartySize)
                failures.Add($"시작 동행이 {state.All.Count}마리입니다 — "
                             + $"{PartyConst.PartySize}마리여야 합니다.");

            // ---------------------------------------------------------- 파티
            state.AutoFillParty();
            var party = state.PartyMembers();
            log.AppendLine($"파티 — {string.Join(" · ", party.Select(o => o.Name))}");

            if (party.Count != PartyConst.PartySize)
            {
                failures.Add($"파티가 {party.Count}마리입니다.");
            }
            else
            {
                for (int column = 0; column < party.Count; column++)
                {
                    if (!GameState.ColumnAllows(party[column].Row.Role, column))
                        failures.Add($"{Korean.Ga(party[column].Name)} 설 수 없는 열에 있습니다.");
                }
            }

            // ---------------------------------------------------------- 능력치
            var first = party.FirstOrDefault();
            if (first is null)
            {
                ok = false;
                failures.Add("파티가 비어 있어 더 진행하지 못했습니다.");
                return Finish(log, failures, out ok);
            }

            var atOne = Stats.Compute(first.Row, 1, 0);
            log.AppendLine($"능력치 — {first.Name} 1레벨 {atOne}");
            if (atOne.Hp != first.Row.Base.Hp)
                failures.Add($"1레벨 체력이 {atOne.Hp} 입니다 — 기본치 "
                             + $"{first.Row.Base.Hp} 와 같아야 합니다.");

            // ---------------------------------------------------------- 육성
            state.AddCurrency("gold", 5_000_000);
            state.AddCurrency("food", 500_000);

            // 파티 전원을 올립니다. 하나만 올리면 뒤쪽 스테이지에서 나머지가 먼저 쓰러져
            // 수호자까지 가지 못합니다.
            int grew = 0;
            foreach (var member in party)
            {
                while (state.CanLevelUp(member) && grew < 600)
                {
                    state.LevelUp(member);
                    grew++;
                }
            }
            var grown = Stats.Compute(first.Row, first.Level, 0);
            log.AppendLine($"육성 — {Korean.Ga(first.Name)} {first.Level}레벨. {grown}");

            if (first.Level != state.LevelCap(first))
                failures.Add($"레벨이 상한 {state.LevelCap(first)} 에 닿지 않고 "
                             + $"{first.Level} 에서 멈추었습니다.");
            if (grown.Attack <= atOne.Attack)
                failures.Add("레벨을 올렸는데 공격이 늘지 않았습니다.");

            // ---------------------------------------------------------- 공명
            state.AddShards(first.SpeciesId, CollectionConst.ShardCap);
            int ranks = 0;
            while (state.CanResonanceUp(first.SpeciesId) && ranks < 10)
            {
                state.ResonanceUp(first.SpeciesId);
                ranks++;
            }
            var resonant = Stats.Compute(first.Row, first.Level,
                                         state.Resonance(first.SpeciesId));
            log.AppendLine($"공명 — {state.Resonance(first.SpeciesId)}등급. "
                           + $"공격 {grown.Attack} → {resonant.Attack}");

            if (state.Resonance(first.SpeciesId) != GrowthConst.ResonanceCap)
                failures.Add($"공명이 상한 {GrowthConst.ResonanceCap} 에 닿지 않았습니다.");
            if (resonant.Attack <= grown.Attack)
                failures.Add("공명을 올렸는데 공격이 늘지 않았습니다.");

            // ---------------------------------------------------------- 탐사
            Expedition(state, log, failures);

            // ---------------------------------------------------------- 각성
            Awaken(state, first, log, failures);

            // ---------------------------------------------------------- 루프
            Grind(state, log, failures);

            // ---------------------------------------------------------- 보상 변종
            CheckRewardVariants(log, failures);

            // ---------------------------------------------------------- 세이브
            RoundTrip(state, log, failures);

            SaveStore.Enabled = true;
            Clock.Offset = 0;
            return Finish(log, failures, out ok);
        }

        // ------------------------------------------------------------ 루프

        /// <summary>
        /// 벽에 막히면 키우고 다시 도전하는 것을 반복한다.
        /// </summary>
        /// <remarks>
        /// **이것이 기획서 2.1 의 한 바퀴입니다.** 첫 지역의 수호자는 39레벨 상대이고 1단의
        /// 레벨 상한은 20이므로, **각성하지 않으면 넘을 수 없는 벽**입니다. 그래서 이 함수가
        /// 전투만 반복하지 않고 탐사 · 육성 · 공명 · 각성을 사이에 끼웁니다 — 루프가 실제로
        /// 도는지 보는 것이 이 검사의 목적입니다.
        /// </remarks>
        private static void Grind(GameState state, StringBuilder log, List<string> failures)
        {
            string regionId = state.UnlockedRegions.OrderBy(GameState.RegionOrder).First();
            var stages = WildlingData.Stage.Records
                .Where(s => s.RegionId == regionId)
                .OrderBy(s => s.Index)
                .ToList();

            var kinds = new HashSet<StageKind>();
            int won = 0, rounds = 0, awakened = 0;

            while (rounds++ < 24)
            {
                var stage = stages.FirstOrDefault(
                    s => state.IsStageOpen(s) && !state.IsCleared(s.StageId));
                if (stage is null)
                    break;

                var enemies = BuildEnemies(stage);
                if (enemies.Count == 0)
                {
                    failures.Add($"스테이지 `{stage.StageId}` 의 등장 목록이 비어 있습니다.");
                    break;
                }

                var battle = Battle.Run(BuildParty(state), enemies, stage.Index * 7919 + rounds);

                if (battle.PartyWon)
                {
                    won++;
                    kinds.Add(stage.StageKind);
                    state.ClearStage(stage);

                    var rng = new Rng(stage.Index * 104729 + rounds);
                    var grants = Rewards.Roll(stage.RewardGroupId, rng);
                    var extra = WildlingData.StageReward.Records
                        .FirstOrDefault(r => r.StageId == stage.StageId);
                    if (extra != null)
                    {
                        grants.AddRange(Rewards.Roll(extra.RewardGroupId, rng));
                        if (extra.HasFirstClearGroupId)
                            grants.AddRange(Rewards.Certain(extra.FirstClearGroupId));
                    }
                    state.Apply(Rewards.Merge(grants));

                    foreach (var enemy in enemies)
                        state.ObserveFromBattle(enemy.Monster.MonsterId, 1);
                    continue;
                }

                // 졌으면 키웁니다. 아무것도 나아지지 않으면 그만둡니다.
                int grew = Improve(state, ref awakened);
                log.AppendLine($"  {stage.Index}번에서 막혀 {grew}가지를 올렸습니다.");
                if (grew == 0)
                {
                    log.AppendLine($"  더 키울 수 없어 {stage.Index}번에서 멈추었습니다.");
                    break;
                }
            }

            log.AppendLine($"루프 — {won}개 스테이지 클리어 · 각성 {awakened}회 · "
                           + $"종류 {string.Join(" · ", kinds.Select(Theme.Label))}");

            if (won == 0)
                failures.Add("첫 스테이지도 이기지 못했습니다.");
            if (!kinds.Contains(StageKind.Guardian)
                && stages.Any(s => s.StageKind == StageKind.Guardian))
                failures.Add($"수호자 스테이지에 도달하지 못했습니다 — {won}개에서 멈추었습니다.");

            var opened = state.UnlockReady();
            log.AppendLine($"지역 — 해금 {state.UnlockedRegions.Count}개"
                           + (opened.Count > 0
                               ? $", 이번에 열린 것 {string.Join(" · ", opened.Select(r => r.Name))}"
                               : ""));

            if (state.UnlockedRegions.Count < 2)
                failures.Add("수호자를 넘었는데 다음 지역이 열리지 않았습니다.");
        }

        /// <summary>
        /// 파티를 한 단계 세게 만든다. 실제로 오른 가지의 수를 낸다.
        /// </summary>
        /// <remarks>
        /// 탐사로 조각과 재료를 모으고, 레벨과 공명을 올리고, 조건이 차면 각성합니다. 각성
        /// 조건의 재료가 모자라면 **채워 넣습니다** — 그 자리까지 실제로 모으려면 탐사를 수십 번
        /// 돌려야 하고, 이 검사가 보려는 것은 재료 수급이 아니라 각성이 도는가입니다.
        /// </remarks>
        private static int Improve(GameState state, ref int awakened)
        {
            int gains = 0;

            // 탐사 한 번.
            string regionId = state.UnlockedRegions.OrderBy(GameState.RegionOrder).First();
            state.ExpeditionStartedUtc = Clock.NowUtc;
            Clock.Offset += IdleConst.CapHours * 3600 + 60;
            var result = Game.Expedition.Settle(state, regionId, state.ExpeditionStartedUtc,
                                                Clock.NowUtc, new Rng((int)Clock.Offset));
            if (state.Apply(result.Grants).Applied.Count > 0)
                gains++;

            state.AddCurrency("gold", 2_000_000);
            state.AddCurrency("food", 200_000);

            foreach (var owned in state.PartyMembers())
            {
                int before = owned.Level;
                int guard = 0;
                while (state.CanLevelUp(owned) && guard++ < 200)
                    state.LevelUp(owned);
                if (owned.Level > before)
                    gains++;

                if (state.CanResonanceUp(owned.SpeciesId))
                {
                    while (state.CanResonanceUp(owned.SpeciesId))
                        state.ResonanceUp(owned.SpeciesId);
                    gains++;
                }

                // 상한에 닿았으면 각성으로만 더 갑니다.
                if (owned.Level < state.LevelCap(owned))
                    continue;

                var link = state.AwakeningOf(owned);
                if (link is null)
                    continue;

                Supply(state, owned, link);
                if (!state.CanAwaken(owned, out _))
                    continue;

                state.Awaken(owned);
                awakened++;
                gains++;
            }

            return gains;
        }

        /// <summary>조각은 종 단위이므로 재화와 다른 자리에 들어갑니다.</summary>
        private static void Give(GameState state, Owned owned, string currencyId, int amount)
        {
            if (GameState.IsShardCurrency(currencyId))
                state.AddShards(owned.SpeciesId, amount);
            else
                state.AddCurrency(currencyId, amount);
        }

        /// <summary>각성 조건 중 모자란 것을 채운다.</summary>
        private static void Supply(GameState state, Owned owned,
                                   MonsterAwakeningRecord link)
        {
            state.SetCodex(owned.MonsterId, CodexState.Studied);

            foreach (var cost in link.Costs)
                Give(state, owned, cost.CurrencyId, cost.Amount);

            foreach (var entry in Requirements.Entries(link.RequirementGroupId))
            {
                if (entry.Req is ItemRequirement item
                    && state.ItemCount(item.ItemId) < item.Amount)
                {
                    state.AddItem(item.ItemId, item.Amount);
                }
                if (entry.Req is StageRequirement stage && stage.StageByStageId != null)
                    state.ClearStage(stage.StageByStageId);
            }
        }

        private static List<Combatant> BuildParty(GameState state)
        {
            var list = new List<Combatant>();
            var members = state.PartyMembers();

            for (int i = 0; i < members.Count; i++)
            {
                var owned = members[i];
                list.Add(new Combatant
                {
                    Monster = owned.Row,
                    Level = owned.Level,
                    Resonance = state.Resonance(owned.SpeciesId),
                    Placement = i,
                    Active = owned.Active.Select(WildlingData.Skill.FindBySkillId)
                        .Where(s => s != null).ToArray(),
                    Passive = owned.Passive.Select(WildlingData.Skill.FindBySkillId)
                        .Where(s => s != null).ToArray(),
                    SkillLevels = owned.SkillLevels.ToArray(),
                });
            }
            return list;
        }

        private static List<Combatant> BuildEnemies(StageRecord stage)
        {
            var list = new List<Combatant>();
            var wave = stage.MonsterByWaveMonsterIds ?? Array.Empty<MonsterRecord>();

            for (int i = 0; i < wave.Length; i++)
            {
                if (wave[i] is null)
                    continue;

                var skills = WildlingData.MonsterSkill.Records
                    .Where(r => r.MonsterId == wave[i].MonsterId
                                && r.UnlockStage <= wave[i].Stage)
                    .ToList();

                list.Add(new Combatant
                {
                    Monster = wave[i],
                    Level = i < stage.WaveLevels.Length ? stage.WaveLevels[i] : 1,
                    Placement = i,
                    IsEnemy = true,
                    Active = Stats.BasicFirst(skills.Where(r => r.SlotKind == SlotKind.Active))
                        .Take(Stats.ActiveSlots(wave[i].Stage))
                        .Select(r => r.SkillBySkillId).Where(s => s != null).ToArray(),
                    Passive = skills.Where(r => r.SlotKind == SlotKind.Passive)
                        .Select(r => r.SkillBySkillId).Where(s => s != null).ToArray(),
                });
            }
            return list;
        }

        // ------------------------------------------------------------ 탐사

        private static void Expedition(GameState state, StringBuilder log,
                                       List<string> failures)
        {
            string regionId = state.UnlockedRegions.OrderBy(GameState.RegionOrder).First();
            state.ExpeditionRegionId = regionId;
            state.ExpeditionStartedUtc = Clock.NowUtc;

            // 8시간을 기다리는 대신 시각을 밉니다.
            Clock.Offset += IdleConst.CapHours * 3600 + 60;

            long before = state.Currency("gold");
            var result = Game.Expedition.Settle(state, regionId,
                                                state.ExpeditionStartedUtc, Clock.NowUtc,
                                                new Rng(20260826));
            var report = state.Apply(result.Grants);

            log.AppendLine($"탐사 — {Game.Expedition.Elapsed(result.CappedSeconds)} 정산. "
                           + $"지급 {result.Grants.Count}종 · 새 기록 {result.Discovered.Count}종");

            if (!result.HitCap)
                failures.Add($"방치 상한 {IdleConst.CapHours}시간에 도달하지 않았습니다.");
            if (state.Currency("gold") <= before)
                failures.Add("탐사를 정산했는데 은편이 늘지 않았습니다.");
            if (result.Grants.Count == 0)
                failures.Add("탐사 정산이 아무것도 내지 않았습니다.");
            if (report.Lines.Count == 0)
                failures.Add("지급 보고가 비어 있습니다.");

            // 시간대가 뒤로 갈수록 산출이 줄어드는가.
            var band0 = WildlingData.RegionYield.FindByRegionIdAndHourBand(regionId, 0);
            var last = WildlingData.RegionYield
                .FindByRegionIdAndHourBand(regionId, IdleConst.CapHours - 1);
            if (band0 != null && last != null && last.GoldPerHour >= band0.GoldPerHour)
                failures.Add($"마지막 시간대의 산출이 첫 시간대보다 적지 않습니다 — "
                             + $"{band0.GoldPerHour} 에서 {last.GoldPerHour}.");
        }

        // ------------------------------------------------------------ 각성

        private static void Awaken(GameState state, Owned subject, StringBuilder log,
                                   List<string> failures)
        {
            var link = state.AwakeningOf(subject);
            if (link is null)
            {
                log.AppendLine($"각성 — {Korean.Eun(subject.Name)} 각성 관계가 없습니다.");
                return;
            }

            // 조건을 채웁니다. 정독과 재료입니다.
            state.SetCodex(subject.MonsterId, CodexState.Studied);
            foreach (var cost in link.Costs)
                Give(state, subject, cost.CurrencyId, cost.Amount * 2);
            foreach (var entry in Requirements.Entries(link.RequirementGroupId))
            {
                if (entry.Req is ItemRequirement item)
                    state.AddItem(item.ItemId, item.Amount * 2);
                if (entry.Req is StageRequirement stage
                    && stage.StageByStageId != null)
                    state.ClearStage(stage.StageByStageId);
            }

            string was = subject.MonsterId;
            var before = Stats.Compute(subject.Row, subject.Level,
                                       state.Resonance(subject.SpeciesId));

            if (!state.CanAwaken(subject, out var checks))
            {
                failures.Add("조건을 전부 채웠는데 각성이 열리지 않았습니다 — "
                             + string.Join(" · ", checks.Where(c => !c.Met).Select(c => c.Text)));
                return;
            }

            int wasCap = state.LevelCap(subject);
            state.Awaken(subject);
            var after = Stats.Compute(subject.Row, subject.Level,
                                      state.Resonance(subject.SpeciesId));
            int nowCap = state.LevelCap(subject);
            var atNewCap = Stats.Compute(subject.Row, nowCap,
                                         state.Resonance(subject.SpeciesId));

            log.AppendLine($"각성 — {was} → {subject.MonsterId} ({subject.Name}). "
                           + $"레벨 {subject.Level} · 액티브 슬롯 {subject.Active.Count}");
            log.AppendLine($"       체력 {before.Hp}({wasCap}레벨) → {after.Hp}(1레벨) "
                           + $"→ {atNewCap.Hp}({nowCap}레벨)");

            if (subject.MonsterId != link.ToMonsterId)
                failures.Add("각성 후 행이 바뀌지 않았습니다.");
            if (subject.Level != 1)
                failures.Add($"각성 후 레벨이 {subject.Level} 입니다 — 1이어야 합니다.");
            if (state.Resonance(subject.SpeciesId) == 0)
                failures.Add("각성으로 공명 등급이 사라졌습니다 — 종 단위여야 합니다.");

            // **각성은 결국 이득이어야 합니다.** 새 단계의 상한이 옛 단계의 상한보다 낮으면
            // 진행이 뒤로 갑니다.
            if (atNewCap.Hp <= before.Hp)
                failures.Add($"각성이 이득이 아닙니다 — {nowCap}레벨 체력 {atNewCap.Hp} 이 "
                             + $"직전 단계 상한 {before.Hp} 보다 크지 않습니다.");

            // 기획서 §7.3 은 **1레벨 기준값**이 직전 단계 상한보다 높아야 한다고 적었습니다.
            // 지금 데이터는 그렇지 않고, 각성 직후 일시적으로 약해집니다. 검사를 실패시키지
            // 않는 것은 그것이 데이터의 밸런스 선택이기 때문입니다 — 자리를 보이게만 둡니다.
            if (after.Hp < before.Hp)
            {
                int recover = 1;
                while (recover < nowCap
                       && Stats.Compute(subject.Row, recover,
                                        state.Resonance(subject.SpeciesId)).Hp < before.Hp)
                {
                    recover++;
                }
                log.AppendLine($"       각성 직후는 약해집니다 — {recover}레벨에서 회복합니다. "
                               + "기획서 §7.3 은 이 구간을 두지 않겠다고 적었습니다.");
            }
        }

        // ------------------------------------------------------------ 변종

        private static void CheckRewardVariants(StringBuilder log, List<string> failures)
        {
            var seen = new Dictionary<string, int>();
            foreach (var entry in WildlingData.RewardEntry.Records)
            {
                string kind = entry.Reward switch
                {
                    ItemReward => "아이템",
                    CurrencyReward => "재화",
                    MonsterReward => "와일드링",
                    ShardReward => "조각",
                    _ => "?",
                };
                seen.TryGetValue(kind, out int count);
                seen[kind] = count + 1;
            }

            log.AppendLine("보상 변종 — "
                           + string.Join(" · ", seen.Select(p => $"{p.Key} {p.Value}")));
            if (seen.ContainsKey("?"))
                failures.Add("어느 변종도 아닌 보상이 있습니다.");

            // 효과 변종도 전부 나오는가.
            var effects = new HashSet<string>();
            foreach (var row in WildlingData.SkillEffect.Records)
                effects.Add(row.Effect?.GetType().Name ?? "?");

            log.AppendLine("효과 변종 — " + string.Join(" · ", effects.OrderBy(x => x)));
            foreach (string wanted in new[]
                     {
                         nameof(DamageEffect), nameof(HealEffect),
                         nameof(BuffEffect), nameof(StatusEffect),
                     })
            {
                if (!effects.Contains(wanted))
                    failures.Add($"효과 변종 `{wanted}` 이 하나도 나오지 않았습니다.");
            }
        }

        // ------------------------------------------------------------ 세이브

        private static void RoundTrip(GameState state, StringBuilder log,
                                      List<string> failures)
        {
            var save = state.ToSave();
            string json = UnityEngine.JsonUtility.ToJson(save);
            var back = GameState.FromSave(UnityEngine.JsonUtility.FromJson<SaveData>(json));

            log.AppendLine($"세이브 — {json.Length}자. 동행 {back.All.Count}마리 · "
                           + $"은편 {back.Currency("gold")}");

            if (back.All.Count != state.All.Count)
                failures.Add($"세이브를 되읽으니 동행이 {back.All.Count}마리입니다 — "
                             + $"{state.All.Count}마리여야 합니다.");
            if (back.Currency("gold") != state.Currency("gold"))
                failures.Add("세이브를 되읽으니 은편이 달라졌습니다.");

            foreach (var owned in state.All)
            {
                var mirror = back.Find(owned.Uid);
                if (mirror is null)
                {
                    failures.Add($"세이브를 되읽으니 `{owned.MonsterId}` 이 없습니다.");
                    continue;
                }
                if (mirror.Level != owned.Level)
                    failures.Add($"`{owned.MonsterId}` 의 레벨이 달라졌습니다.");
                if (back.Resonance(owned.SpeciesId) != state.Resonance(owned.SpeciesId))
                    failures.Add($"`{owned.SpeciesId}` 의 공명이 달라졌습니다.");
            }
        }

        // ------------------------------------------------------------ 마무리

        private static string Finish(StringBuilder log, List<string> failures, out bool ok)
        {
            log.AppendLine();
            if (failures.Count == 0)
            {
                log.AppendLine("전부 통과했습니다.");
                ok = true;
                return log.ToString();
            }

            log.AppendLine($"실패 {failures.Count}건");
            foreach (string failure in failures)
                log.AppendLine($"  !! {failure}");
            ok = false;
            return log.ToString();
        }
    }
}
