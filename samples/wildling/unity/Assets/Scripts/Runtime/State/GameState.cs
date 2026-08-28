using System;
using System.Collections.Generic;
using System.Linq;
using Wildling.Data;

namespace Wildling.Game
{
    /// <summary>동행 중인 개체 하나이다.</summary>
    public sealed class Owned
    {
        public int Uid;
        public string MonsterId;
        public int Level = 1;
        public List<string> Active = new();
        public List<string> Passive = new();
        public List<int> SkillLevels = new();

        public MonsterRecord Row => WildlingData.Monster.FindByMonsterId(MonsterId);
        public string SpeciesId => Row?.SpeciesId ?? MonsterId;
        public string Name => Row?.Name ?? MonsterId;
    }

    /// <summary>지급하고 나서 무슨 일이 있었는가.</summary>
    public sealed class GrantReport
    {
        public readonly List<string> Lines = new();
        public readonly List<string> NewMonsters = new();
        public readonly List<Grant> Applied = new();
        public bool Any => Lines.Count > 0;
    }

    /// <summary>
    /// 진행 상태와 그 위에서 도는 규칙이다.
    /// </summary>
    /// <remarks>
    /// **표에서 읽은 값을 여기 복사해 두지 않습니다.** 상태는 「무엇을 얼마나 가졌는가」까지이고
    /// 「그것이 얼마나 센가」는 매번 표에서 계산합니다.
    /// </remarks>
    public sealed class GameState
    {
        private readonly Dictionary<string, long> _currencies = new();
        private readonly Dictionary<string, int> _items = new();
        private readonly Dictionary<string, int> _shards = new();
        private readonly Dictionary<string, int> _resonance = new();
        private readonly Dictionary<string, (int Observed, CodexState State)> _codex = new();
        private readonly Dictionary<string, int> _regionProgress = new();

        private readonly List<Owned> _owned = new();
        private readonly HashSet<string> _unlocked = new();
        private readonly HashSet<string> _firstClears = new();
        private readonly HashSet<string> _claimedCodex = new();
        private readonly List<int[]> _parties = new();

        private int _nextUid = 1;

        public int ActiveParty;
        public string ExpeditionRegionId = "";
        public long ExpeditionStartedUtc;
        public long LastSeenUtc;

        public IReadOnlyList<Owned> All => _owned;
        public IReadOnlyCollection<string> UnlockedRegions => _unlocked;

        // ------------------------------------------------------------ 재화

        public long Currency(string id) => _currencies.TryGetValue(id, out long v) ? v : 0;

        public void AddCurrency(string id, long amount)
        {
            long cap = WildlingData.Currency.FindByCurrencyId(id)?.Cap ?? long.MaxValue;
            _currencies[id] = Math.Min(cap, Math.Max(0, Currency(id) + amount));
        }

        public bool SpendCurrency(string id, long amount)
        {
            if (Currency(id) < amount)
                return false;
            _currencies[id] = Currency(id) - amount;
            return true;
        }

        public int ItemCount(string id) => _items.TryGetValue(id, out int v) ? v : 0;

        public void AddItem(string id, int amount)
        {
            int cap = WildlingData.Item.FindByItemId(id)?.StackMax ?? int.MaxValue;
            _items[id] = Math.Min(cap, Math.Max(0, ItemCount(id) + amount));
        }

        public bool SpendItem(string id, int amount)
        {
            if (ItemCount(id) < amount)
                return false;
            _items[id] = ItemCount(id) - amount;
            return true;
        }

        /// <summary>조각은 종 단위이다. 각성으로 `monster_id` 가 바뀌어도 남아야 한다.</summary>
        public int Shards(string speciesId) => _shards.TryGetValue(speciesId, out int v) ? v : 0;

        public void AddShards(string speciesId, int amount)
            => _shards[speciesId] = Math.Min(CollectionConst.ShardCap,
                                             Math.Max(0, Shards(speciesId) + amount));

        public bool SpendShards(string speciesId, int amount)
        {
            if (Shards(speciesId) < amount)
                return false;
            _shards[speciesId] = Shards(speciesId) - amount;
            return true;
        }

        // ------------------------------------------------------------ 지급

        public GrantReport Apply(IEnumerable<Grant> grants)
        {
            var report = new GrantReport();

            foreach (var grant in grants)
            {
                if (grant.Amount <= 0)
                    continue;

                switch (grant.Kind)
                {
                    case GrantKind.Currency:
                        AddCurrency(grant.Id, grant.Amount);
                        report.Lines.Add(Rewards.Describe(grant));
                        report.Applied.Add(grant);
                        break;

                    case GrantKind.Item:
                        AddItem(grant.Id, grant.Amount);
                        report.Lines.Add(Rewards.Describe(grant));
                        report.Applied.Add(grant);
                        break;

                    case GrantKind.Shard:
                    {
                        var row = WildlingData.Monster.FindByMonsterId(grant.Id);
                        AddShards(row?.SpeciesId ?? grant.Id, grant.Amount);
                        report.Lines.Add(Rewards.Describe(grant));
                        report.Applied.Add(grant);
                        break;
                    }

                    case GrantKind.Monster:
                        Discover(grant.Id, grant.Amount, report);
                        break;
                }
            }

            return report;
        }

        /// <summary>
        /// 종 하나를 발견한다. 이미 동행 중이면 조각으로 전환한다.
        /// </summary>
        /// <remarks>
        /// **같은 종의 다른 단계는 중복이 아닙니다.** 기록부가 행 단위이므로 새 기록으로
        /// 처리하고, 동행 개체의 단계는 변하지 않습니다 — 기획서 5.4 의 마지막 줄입니다.
        /// </remarks>
        public void Discover(string monsterId, int count, GrantReport report)
        {
            var row = WildlingData.Monster.FindByMonsterId(monsterId);
            if (row is null)
                return;

            bool hadRecord = CodexState(monsterId) >= Data.CodexState.Recorded;
            SetCodex(monsterId, Data.CodexState.Recorded);

            bool haveSpecies = _owned.Any(o => o.SpeciesId == row.SpeciesId);

            if (!haveSpecies)
            {
                Own(monsterId);
                report.NewMonsters.Add(monsterId);
                report.Lines.Add($"{Korean.Ga(row.Name)} 동행하게 되었습니다.");
                count--;
            }
            else if (!hadRecord)
            {
                report.NewMonsters.Add(monsterId);
                report.Lines.Add($"{row.Name} 의 기록이 열렸습니다.");
                count--;
            }

            if (count <= 0)
                return;

            // 중복은 조각입니다. 전환량은 등급이 정합니다.
            int per = ShardsPerDuplicate(row.Grade);
            AddShards(row.SpeciesId, per * count);
            Observe(monsterId, count);
            report.Lines.Add($"{row.Name} 울림 조각 ×{per * count}");
        }

        /// <summary>중복 하나가 몇 조각이 되는가. 등급이 높을수록 많다.</summary>
        public static int ShardsPerDuplicate(Grade grade) => grade switch
        {
            Grade.Common => 4,
            Grade.Rare => 8,
            Grade.Epic => 16,
            Grade.Legendary => 30,
            _ => 50,
        };

        // ------------------------------------------------------------ 기록부

        public CodexState CodexState(string monsterId)
            => _codex.TryGetValue(monsterId, out var e) ? e.State : Data.CodexState.Unknown;

        public int Observed(string monsterId)
            => _codex.TryGetValue(monsterId, out var e) ? e.Observed : 0;

        public static int ObserveCap(Grade grade) => grade switch
        {
            Grade.Common => CodexConst.ObserveCapCommon,
            Grade.Rare => CodexConst.ObserveCapRare,
            Grade.Epic => CodexConst.ObserveCapEpic,
            Grade.Legendary => CodexConst.ObserveCapLegendary,
            _ => CodexConst.ObserveCapMythic,
        };

        /// <summary>상태를 올린다. 내려가지는 않는다.</summary>
        public void SetCodex(string monsterId, CodexState state)
        {
            var entry = _codex.TryGetValue(monsterId, out var e) ? e : (0, Data.CodexState.Unknown);
            if (state > entry.Item2)
                entry.Item2 = state;
            _codex[monsterId] = entry;
        }

        /// <summary>관측을 누적한다. 상한에 닿으면 정독이 된다.</summary>
        public void Observe(string monsterId, int count)
        {
            var row = WildlingData.Monster.FindByMonsterId(monsterId);
            if (row is null || count <= 0)
                return;
            if (CodexState(monsterId) < Data.CodexState.Recorded)
                return;

            int cap = ObserveCap(row.Grade);
            var entry = _codex[monsterId];
            entry.Observed = Math.Min(cap, entry.Observed + count);
            if (entry.Observed >= cap)
                entry.State = Data.CodexState.Studied;
            _codex[monsterId] = entry;
        }

        /// <summary>전투 승리의 관측이다. 계수가 붙으므로 소수가 잘려 나갈 수 있다.</summary>
        public void ObserveFromBattle(string monsterId, int wins)
            => Observe(monsterId, Numbers.Apply(wins, CodexConst.BattleObserveFactor));

        /// <summary>기록 이상인 행의 비율이다. 만분율이다.</summary>
        public int Completion(string regionId = null)
        {
            var rows = WildlingData.Monster.Records.AsEnumerable();
            if (!string.IsNullOrEmpty(regionId))
            {
                long mask = 1L << (RegionOrder(regionId) - 1);
                rows = rows.Where(r => (r.Habitat & mask) != 0);
            }

            var list = rows.ToList();
            if (list.Count == 0)
                return 0;
            int recorded = list.Count(r => CodexState(r.MonsterId) >= Data.CodexState.Recorded);
            return (int)((long)recorded * Numbers.One / list.Count);
        }

        /// <summary>서식 비트가 몇째 자리인가. `Region.order` 가 그 자리이다.</summary>
        public static int RegionOrder(string regionId)
            => WildlingData.Region.FindByRegionId(regionId)?.Order ?? 1;

        // ------------------------------------------------------------ 동행

        public Owned Find(int uid) => _owned.FirstOrDefault(o => o.Uid == uid);

        public Owned OfSpecies(string speciesId)
            => _owned.FirstOrDefault(o => o.SpeciesId == speciesId);

        public Owned Own(string monsterId)
        {
            var row = WildlingData.Monster.FindByMonsterId(monsterId);
            if (row is null)
                return null;

            var owned = new Owned { Uid = _nextUid++, MonsterId = monsterId, Level = 1 };
            FitSkills(owned);
            _owned.Add(owned);
            SetCodex(monsterId, Data.CodexState.Recorded);
            return owned;
        }

        /// <summary>
        /// 그 단계가 쓸 수 있는 스킬로 슬롯을 채운다.
        /// </summary>
        /// <remarks>
        /// `MonsterSkill` 이 후보를 정하고 `AwakeningConst` 가 개수를 정합니다. 플레이어가
        /// 고르는 자리이지만, 비어 있으면 전투가 성립하지 않으므로 기본값을 여기서 채웁니다.
        /// </remarks>
        public void FitSkills(Owned owned)
        {
            var row = owned.Row;
            if (row is null)
                return;

            var usable = WildlingData.MonsterSkill.Records
                .Where(r => r.MonsterId == owned.MonsterId && r.UnlockStage <= row.Stage)
                .ToList();

            // 대기 없는 스킬이 앞에 오게 합니다 — 그래야 슬롯이 전부 대기에 걸리지 않습니다.
            var actives = Stats.BasicFirst(usable.Where(r => r.SlotKind == SlotKind.Active))
                               .Select(r => r.SkillId).ToList();
            var passives = usable.Where(r => r.SlotKind == SlotKind.Passive)
                                 .Select(r => r.SkillId).ToList();

            // 이미 고른 것은 남기고 모자란 만큼만 채웁니다.
            owned.Active = Keep(owned.Active, actives, Stats.ActiveSlots(row.Stage));
            owned.Passive = Keep(owned.Passive, passives, Stats.PassiveSlots(row.Stage));

            while (owned.SkillLevels.Count < owned.Active.Count)
                owned.SkillLevels.Add(1);
            while (owned.SkillLevels.Count > owned.Active.Count)
                owned.SkillLevels.RemoveAt(owned.SkillLevels.Count - 1);

            static List<string> Keep(List<string> chosen, List<string> pool, int slots)
            {
                var kept = chosen.Where(pool.Contains).Distinct().Take(slots).ToList();
                foreach (string id in pool)
                {
                    if (kept.Count >= slots)
                        break;
                    if (!kept.Contains(id))
                        kept.Add(id);
                }
                return kept;
            }
        }

        // ------------------------------------------------------------ 성장

        public int LevelCap(Owned owned) => Stats.LevelCap(owned.Row?.Stage ?? 1);

        public bool CanLevelUp(Owned owned)
        {
            if (owned is null || owned.Level >= LevelCap(owned))
                return false;
            foreach (var cost in Stats.LevelCost(owned.Row.Grade, owned.Level + 1))
            {
                if (Currency(cost.CurrencyId) < cost.Amount)
                    return false;
            }
            return true;
        }

        public bool LevelUp(Owned owned)
        {
            if (!CanLevelUp(owned))
                return false;
            foreach (var cost in Stats.LevelCost(owned.Row.Grade, owned.Level + 1))
                SpendCurrency(cost.CurrencyId, cost.Amount);
            owned.Level++;
            return true;
        }

        public int Resonance(string speciesId)
            => _resonance.TryGetValue(speciesId, out int v) ? v : 0;

        public bool CanResonanceUp(string speciesId)
        {
            int next = Resonance(speciesId) + 1;
            if (next > GrowthConst.ResonanceCap)
                return false;
            var any = OfSpecies(speciesId);
            if (any?.Row is null)
                return false;
            return Shards(speciesId) >= Stats.ResonanceCost(any.Row.Grade, next);
        }

        public bool ResonanceUp(string speciesId)
        {
            if (!CanResonanceUp(speciesId))
                return false;
            int next = Resonance(speciesId) + 1;
            var any = OfSpecies(speciesId);
            SpendShards(speciesId, Stats.ResonanceCost(any.Row.Grade, next));
            _resonance[speciesId] = next;
            return true;
        }

        // ------------------------------------------------------------ 각성

        public MonsterAwakeningRecord AwakeningOf(Owned owned)
            => owned is null
                ? null
                : WildlingData.MonsterAwakening.Records
                    .FirstOrDefault(r => r.FromMonsterId == owned.MonsterId);

        /// <summary>
        /// 그 비용이 종 단위인가.
        /// </summary>
        /// <remarks>
        /// **조각은 종의 것입니다**(기획서 5.4). 그런데 `MonsterAwakening.costs` 는 재화로
        /// 적히고 `Currency` 에 `shard` 행이 있습니다. 어느 재화가 종 단위인지를 코드에 고정하지
        /// 않으려고 `CollectionConst.ShardCurrency` 에 두었습니다 — 재화의 이름이 바뀌면
        /// 시트에서 따라갑니다.
        /// </remarks>
        public static bool IsShardCurrency(string currencyId)
            => currencyId == CollectionConst.ShardCurrency;

        public long CostBalance(Owned owned, string currencyId)
            => IsShardCurrency(currencyId) ? Shards(owned.SpeciesId) : Currency(currencyId);

        public bool CanAwaken(Owned owned, out List<Requirements.Check> checks)
        {
            checks = new List<Requirements.Check>();
            var link = AwakeningOf(owned);
            if (link is null)
                return false;

            checks = Requirements.Evaluate(link.RequirementGroupId, this, owned);

            // 재료는 `costs` 이고 **멀티 로우이므로 원소가 여럿입니다.**
            foreach (var cost in link.Costs)
            {
                long have = CostBalance(owned, cost.CurrencyId);
                checks.Add(new Requirements.Check
                {
                    Text = $"{WildlingData.Currency.FindByCurrencyId(cost.CurrencyId)?.Name ?? cost.CurrencyId}"
                           + $" {cost.Amount} (지금 {have})",
                    Met = have >= cost.Amount,
                });
            }

            return checks.All(c => c.Met);
        }

        public bool Awaken(Owned owned)
        {
            if (!CanAwaken(owned, out _))
                return false;

            var link = AwakeningOf(owned);
            foreach (var cost in link.Costs)
            {
                if (IsShardCurrency(cost.CurrencyId))
                    SpendShards(owned.SpeciesId, cost.Amount);
                else
                    SpendCurrency(cost.CurrencyId, cost.Amount);
            }

            owned.MonsterId = link.ToMonsterId;
            owned.Level = 1;
            FitSkills(owned);

            // 각성 직후 기록부에 다음 단계 행이 **기록** 상태로 열립니다.
            SetCodex(link.ToMonsterId, Data.CodexState.Recorded);
            return true;
        }

        // ------------------------------------------------------------ 파티

        public int[] Party(int index)
        {
            while (_parties.Count <= index)
                _parties.Add(new int[PartyConst.PartySize]);
            return _parties[index];
        }

        public List<Owned> PartyMembers(int index = -1)
            => Party(index < 0 ? ActiveParty : index)
                .Select(Find).Where(o => o != null).ToList();

        /// <summary>그 역할이 설 수 있는 열인가. `PartyConst` 가 정한다.</summary>
        public static bool ColumnAllows(Role role, int column)
        {
            if (column < 0 || column >= PartyConst.ColumnNames.Length)
                return false;
            string name = PartyConst.ColumnNames[column];
            string[] allowed = role switch
            {
                Role.Vanguard => PartyConst.VanguardColumns,
                Role.Breaker => PartyConst.BreakerColumns,
                Role.Warden => PartyConst.WardenColumns,
                _ => PartyConst.TunerColumns,
            };
            return allowed.Contains(name);
        }

        public bool SetPartySlot(int partyIndex, int column, int uid)
        {
            var slots = Party(partyIndex);
            if (column < 0 || column >= slots.Length)
                return false;

            if (uid != 0)
            {
                var owned = Find(uid);
                if (owned?.Row is null || !ColumnAllows(owned.Row.Role, column))
                    return false;
                for (int i = 0; i < slots.Length; i++)
                {
                    if (slots[i] == uid)
                        slots[i] = 0;
                }
            }

            slots[column] = uid;
            return true;
        }

        /// <summary>
        /// 열마다 가장 나은 개체로 채운다. 바뀐 자리의 수를 낸다.
        /// </summary>
        /// <remarks>
        /// **공격력만 보면 수호 역할이 뽑히지 않습니다.** 전력은 체력·공격·방어·속도를 함께
        /// 보고, 상대가 정해져 있으면 **상성까지** 봅니다 — 같은 개체라도 무엇과 싸우느냐에
        /// 따라 답이 달라집니다. 그래서 진행한 뒤 다시 누르면 다른 답이 나옵니다.
        /// </remarks>
        public int AutoFillParty(int partyIndex = -1, StageRecord against = null)
        {
            int index = partyIndex < 0 ? ActiveParty : partyIndex;
            var slots = Party(index);
            var before = slots.ToArray();

            var foes = (against?.MonsterByWaveMonsterIds
                        ?? Array.Empty<MonsterRecord>())
                .Where(m => m != null)
                .Select(m => m.Element)
                .Distinct()
                .ToList();

            for (int i = 0; i < slots.Length; i++)
                slots[i] = 0;

            for (int column = 0; column < slots.Length; column++)
            {
                var pick = _owned
                    .Where(o => o.Row != null && ColumnAllows(o.Row.Role, column))
                    .Where(o => !slots.Contains(o.Uid))
                    .OrderByDescending(o => Score(o, foes))
                    .ThenBy(o => o.Uid)
                    .FirstOrDefault();
                slots[column] = pick?.Uid ?? 0;
            }

            int changed = 0;
            for (int i = 0; i < slots.Length; i++)
            {
                if (slots[i] != before[i])
                    changed++;
            }
            return changed;
        }

        /// <summary>그 개체가 그 상대에게 얼마나 쓸 만한가.</summary>
        private long Score(Owned owned, List<Element> foes)
        {
            long power = Diagnose.Power(
                Stats.Compute(owned.Row, owned.Level, Resonance(owned.SpeciesId)));

            if (foes.Count == 0)
                return power;

            long offense = 0, defense = 0;
            foreach (var foe in foes)
            {
                offense += WildlingData.ElementAffinity
                    .FindByAttackerAndDefender(owned.Row.Element, foe)?.Factor
                    ?? BattleConst.NeutralAffinity;
                defense += WildlingData.ElementAffinity
                    .FindByAttackerAndDefender(foe, owned.Row.Element)?.Factor
                    ?? BattleConst.NeutralAffinity;
            }
            offense /= foes.Count;
            defense /= foes.Count;

            // 때리기 좋고 맞기 나쁜 쪽이 높습니다.
            return power * offense / Numbers.One
                        * (2L * Numbers.One - defense) / Numbers.One;
        }

        // ------------------------------------------------------------ 지역과 스테이지

        public bool IsUnlocked(string regionId) => _unlocked.Contains(regionId);

        public void Unlock(string regionId) => _unlocked.Add(regionId);

        public int HighestCleared(string regionId)
            => _regionProgress.TryGetValue(regionId, out int v) ? v : 0;

        public bool IsStageOpen(StageRecord stage)
            => stage != null
               && IsUnlocked(stage.RegionId)
               && stage.Index <= HighestCleared(stage.RegionId) + 1;

        public bool IsCleared(string stageId) => _firstClears.Contains(stageId);

        /// <summary>스테이지를 클리어로 기록한다. 첫 클리어이면 참이다.</summary>
        public bool ClearStage(StageRecord stage)
        {
            bool first = _firstClears.Add(stage.StageId);
            if (stage.Index > HighestCleared(stage.RegionId))
                _regionProgress[stage.RegionId] = stage.Index;
            return first;
        }

        /// <summary>해금 조건을 만족한 지역을 연다. 새로 열린 지역을 낸다.</summary>
        public List<RegionRecord> UnlockReady()
        {
            var opened = new List<RegionRecord>();
            foreach (var region in WildlingData.Region.Records.OrderBy(r => r.Order))
            {
                if (IsUnlocked(region.RegionId))
                    continue;
                if (!region.HasRequirementGroupId)
                {
                    Unlock(region.RegionId);
                    opened.Add(region);
                    continue;
                }
                if (Requirements.Met(region.RequirementGroupId, this, null))
                {
                    Unlock(region.RegionId);
                    opened.Add(region);
                }
            }
            return opened;
        }

        /// <summary>아직 받지 않은 기록부 완성 보상을 낸다.</summary>
        public List<CodexRewardRecord> PendingCodexRewards()
            => WildlingData.CodexReward.Records
                .Where(r => !_claimedCodex.Contains(r.CodexRewardId))
                .Where(r => Completion(r.HasRegionId ? r.RegionId : null)
                            >= r.Threshold * (Numbers.One / 100))
                .ToList();

        public void MarkCodexClaimed(string codexRewardId) => _claimedCodex.Add(codexRewardId);

        // ------------------------------------------------------------ 세이브

        /// <summary>
        /// 새 판을 연다.
        /// </summary>
        /// <remarks>
        /// **첫 지급도 표에 있습니다.** `NewGameConst.RewardGroup` 이 가리키는 묶음을 그대로
        /// 지급하므로, 시작 재화를 바꾸는 것이 시트 수정으로 끝납니다 — 코드에 숫자가 없습니다.
        ///
        /// **탐사도 이미 돌고 있습니다.** 새 판을 열자마자 정산할 것이 있어야 루프의 첫 바퀴가
        /// 「기다리기」로 시작하지 않습니다. 그 시간도 `NewGameConst.ExpeditionHours` 입니다.
        /// </remarks>
        public static GameState NewGame(long nowUtc)
        {
            var state = new GameState { LastSeenUtc = nowUtc };

            // 첫 지역과 그 지역의 시작 동행입니다.
            foreach (var region in WildlingData.Region.Records)
            {
                if (region.State == RegionState.Open)
                    state.Unlock(region.RegionId);
            }
            state.UnlockReady();

            var starters = WildlingData.Monster.Records
                .Where(m => m.Stage == 1)
                .Where(m => (m.Habitat & 1L) != 0)
                .OrderBy(m => m.Grade)
                .ThenBy(m => m.MonsterId)
                .GroupBy(m => m.Role)
                .Select(g => g.First())
                .Take(PartyConst.PartySize)
                .ToList();

            foreach (var starter in starters)
                state.Own(starter.MonsterId);

            state.AutoFillParty();

            state.Apply(Rewards.Certain(NewGameConst.RewardGroup));

            // 탐사가 이미 돌고 있습니다. 열자마자 정산할 것이 있습니다.
            state.ExpeditionRegionId = WildlingData.Region.Records
                .Where(r => state.IsUnlocked(r.RegionId))
                .OrderBy(r => r.Order)
                .Select(r => r.RegionId)
                .FirstOrDefault() ?? "";
            if (!string.IsNullOrEmpty(state.ExpeditionRegionId))
                state.ExpeditionStartedUtc = nowUtc - NewGameConst.ExpeditionHours * 3600L;

            return state;
        }

        public SaveData ToSave()
        {
            var save = new SaveData
            {
                DataStamp = $"monster:{WildlingData.Monster.Records.Count}"
                            + $" skill:{WildlingData.Skill.Records.Count}",
                ActiveParty = ActiveParty,
                ExpeditionRegionId = ExpeditionRegionId,
                ExpeditionStartedUtc = ExpeditionStartedUtc,
                LastSeenUtc = LastSeenUtc,
                NextUid = _nextUid,
            };

            save.Currencies.AddRange(_currencies.Select(p => Pair(p.Key, p.Value)));
            save.Items.AddRange(_items.Select(p => Pair(p.Key, p.Value)));
            save.Shards.AddRange(_shards.Select(p => Pair(p.Key, p.Value)));
            save.Resonances.AddRange(_resonance.Select(p => Pair(p.Key, p.Value)));
            save.RegionProgress.AddRange(_regionProgress.Select(p => Pair(p.Key, p.Value)));

            save.UnlockedRegions.AddRange(_unlocked);
            save.FirstClears.AddRange(_firstClears);
            save.ClaimedCodexRewards.AddRange(_claimedCodex);

            foreach (var owned in _owned)
            {
                save.Owned.Add(new SaveData.OwnedRow
                {
                    Uid = owned.Uid,
                    MonsterId = owned.MonsterId,
                    Level = owned.Level,
                    Active = new List<string>(owned.Active),
                    Passive = new List<string>(owned.Passive),
                    SkillLevels = new List<int>(owned.SkillLevels),
                });
            }

            foreach (var entry in _codex)
            {
                save.Codex.Add(new SaveData.CodexRow
                {
                    MonsterId = entry.Key,
                    Observed = entry.Value.Observed,
                    State = (int)entry.Value.State,
                });
            }

            foreach (var party in _parties)
                save.Parties.Add(new SaveData.PartyRow { Slots = party.ToList() });

            return save;

            static SaveData.Pair Pair(string key, long value)
                => new() { Key = key, Value = value };
        }

        public static GameState FromSave(SaveData save)
        {
            var state = new GameState
            {
                ActiveParty = save.ActiveParty,
                ExpeditionRegionId = save.ExpeditionRegionId ?? "",
                ExpeditionStartedUtc = save.ExpeditionStartedUtc,
                LastSeenUtc = save.LastSeenUtc,
                _nextUid = Math.Max(1, save.NextUid),
            };

            foreach (var p in save.Currencies) state._currencies[p.Key] = p.Value;
            foreach (var p in save.Items) state._items[p.Key] = (int)p.Value;
            foreach (var p in save.Shards) state._shards[p.Key] = (int)p.Value;
            foreach (var p in save.Resonances) state._resonance[p.Key] = (int)p.Value;
            foreach (var p in save.RegionProgress) state._regionProgress[p.Key] = (int)p.Value;

            foreach (string id in save.UnlockedRegions) state._unlocked.Add(id);
            foreach (string id in save.FirstClears) state._firstClears.Add(id);
            foreach (string id in save.ClaimedCodexRewards) state._claimedCodex.Add(id);

            foreach (var row in save.Owned)
            {
                // **표에서 사라진 행은 버립니다.** 데이터가 바뀌어 그 종이 없어졌을 수 있습니다.
                if (WildlingData.Monster.FindByMonsterId(row.MonsterId) is null)
                    continue;

                var owned = new Owned
                {
                    Uid = row.Uid,
                    MonsterId = row.MonsterId,
                    Level = row.Level,
                    Active = new List<string>(row.Active ?? new List<string>()),
                    Passive = new List<string>(row.Passive ?? new List<string>()),
                    SkillLevels = new List<int>(row.SkillLevels ?? new List<int>()),
                };
                state.FitSkills(owned);
                state._owned.Add(owned);
                state._nextUid = Math.Max(state._nextUid, row.Uid + 1);
            }

            foreach (var row in save.Codex)
            {
                if (WildlingData.Monster.FindByMonsterId(row.MonsterId) is null)
                    continue;
                state._codex[row.MonsterId] = (row.Observed, (CodexState)row.State);
            }

            foreach (var party in save.Parties)
            {
                var slots = new int[PartyConst.PartySize];
                for (int i = 0; i < slots.Length && i < party.Slots.Count; i++)
                    slots[i] = party.Slots[i];
                state._parties.Add(slots);
            }

            // 레벨 상한이 내려갔을 수 있습니다.
            foreach (var owned in state._owned)
                owned.Level = Numbers.Clamp(owned.Level, 1, state.LevelCap(owned));

            return state;
        }
    }
}
