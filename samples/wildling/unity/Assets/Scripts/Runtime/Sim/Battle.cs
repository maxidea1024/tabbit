using System;
using System.Collections.Generic;
using System.Linq;
using Wildling.Data;

namespace Wildling.Game
{
    /// <summary>전투에 선 개체 하나이다.</summary>
    public sealed class Combatant
    {
        public MonsterRecord Monster;
        public int Level;
        public int Resonance;
        public int Placement;      // 배치 순서. 속도가 같을 때의 순서이다
        public bool IsEnemy;

        public StatBlock Base;
        public int Hp;
        public int MaxHp;

        public SkillRecord[] Active = Array.Empty<SkillRecord>();
        public SkillRecord[] Passive = Array.Empty<SkillRecord>();
        public int[] SkillLevels = Array.Empty<int>();

        public int[] Cooldowns = Array.Empty<int>();
        public int NextSlot;
        public int BossStatFactor = Numbers.One;

        public readonly List<Buff> Buffs = new();
        public readonly Dictionary<StatusKind, int> Statuses = new();

        public bool Alive => Hp > 0;
        public string Name => Monster.Name;

        public struct Buff
        {
            public StatKind Stat;
            public int Ratio;      // 만분율. 음수는 하락이다
            public int Turns;
        }

        /// <summary>버프를 적용한 뒤의 능력치이다.</summary>
        public int Stat(StatKind kind)
        {
            int value = kind switch
            {
                StatKind.Attack => Base.Attack,
                StatKind.Defense => Base.Defense,
                StatKind.Speed => Base.Speed,
                _ => Base.CritRate,
            };
            value = Numbers.Apply(value, BossStatFactor);

            int total = Numbers.One;
            foreach (var buff in Buffs)
            {
                if (buff.Stat == kind)
                    total += buff.Ratio;
            }
            // 느려짐은 속도를 절반으로 만듭니다. 순서를 회마다 다시 정하므로 실제로 늦어집니다.
            if (kind == StatKind.Speed && Statuses.ContainsKey(StatusKind.Slow))
                total /= 2;

            return Math.Max(1, Numbers.Apply(value, Math.Max(1000, total)));
        }
    }

    /// <summary>한 박자가 무엇인가. 화면이 이것으로 연출을 고른다.</summary>
    public enum BeatKind
    {
        Line,
        Act,
        Damage,
        Heal,
        Buff,
        Status,
        Down,
        Miss,
    }

    /// <summary>전투 한 판의 기록이다.</summary>
    /// <remarks>
    /// **글과 그 시점의 체력을 함께 듭니다.** 화면이 기록을 순서대로 재생하면서 막대도 그때의
    /// 값으로 그릴 수 있어야 하기 때문입니다 — 마지막 값만 들고 있으면 재생이 시작하자마자
    /// 결과가 보입니다.
    /// </remarks>
    public sealed class BattleReport
    {
        public bool PartyWon;
        public int Turns;
        public bool DecidedByHealth;
        public readonly List<Beat> Beats = new();
        public readonly List<Combatant> Party = new();
        public readonly List<Combatant> Enemies = new();

        public struct Beat
        {
            public string Text;
            public BeatKind Kind;

            /// <summary>움직인 쪽. 없으면 -1 이다.</summary>
            public bool ActorIsEnemy;
            public int ActorIndex;

            /// <summary>받은 쪽. 없으면 -1 이다.</summary>
            public bool TargetIsEnemy;
            public int TargetIndex;

            public int Amount;
            public bool Crit;

            /// <summary>무엇을 했는가. 스킬 이름이거나 「패스」 같은 짧은 말이다.</summary>
            public string Note;

            /// <summary>그 스킬의 아이콘. 없으면 빈 문자열이다.</summary>
            public string Icon;

            /// <summary>그 한 방의 상성 배수. 만분율이고 0이면 해당 없음이다.</summary>
            public int Affinity;

            public int[] PartyHp;
            public int[] EnemyHp;
        }

        /// <summary>한 줄을 남기고 그 순간의 체력을 함께 담는다.</summary>
        public void Say(string text) => Say(text, BeatKind.Line, null, null);

        /// <summary>
        /// 누가 누구에게 무엇을 했는지까지 남긴다.
        /// </summary>
        /// <remarks>
        /// **화면이 이것으로 연출합니다.** 글만 흐르면 무엇이 일어났는지 읽어야 알 수 있으므로,
        /// 맞은 쪽이 흔들리고 피해 숫자가 뜨도록 자리와 수치를 함께 답니다. 계산에는 영향이
        /// 없습니다 — 기록에 칸이 늘었을 뿐입니다.
        /// </remarks>
        public void Say(string text, BeatKind kind, Combatant actor, Combatant target,
                        int amount = 0, bool crit = false, string note = null,
                        string icon = null, int affinity = 0)
            => Beats.Add(new Beat
            {
                Affinity = affinity,
                Text = text,
                Kind = kind,
                Note = note ?? "",
                Icon = icon ?? "",
                ActorIsEnemy = actor?.IsEnemy ?? false,
                ActorIndex = IndexOf(actor),
                TargetIsEnemy = target?.IsEnemy ?? false,
                TargetIndex = IndexOf(target),
                Amount = amount,
                Crit = crit,
                PartyHp = Party.Select(c => c.Hp).ToArray(),
                EnemyHp = Enemies.Select(c => c.Hp).ToArray(),
            });

        private int IndexOf(Combatant c)
        {
            if (c is null)
                return -1;
            return c.IsEnemy ? Enemies.IndexOf(c) : Party.IndexOf(c);
        }
    }

    /// <summary>
    /// 자동 전투이다. 기획서 9.3 그대로이다.
    /// </summary>
    /// <remarks>
    /// **입력이 없습니다.** 편성이 결정하고 그 뒤는 관전입니다. 그래서 이 클래스는 화면과
    /// 무관하게 끝까지 돌고, 화면은 그 기록을 순서대로 재생하기만 합니다 — 배속이 계산을
    /// 바꾸지 않는 이유입니다.
    ///
    /// **피해 계산이 기획서의 글자 그대로가 아닙니다.** 기획서 9.3은
    /// `(공격 × 스킬 배수 - 방어 × 방어계수)` 로 적었지만 `BattleConst.DefenseFactor` 가
    /// 만분율 60이므로 그대로 빼면 방어가 피해를 0.2 깎습니다. 컬럼 설명이
    /// 「방어가 피해를 깎는 **비율**」이므로 비율로 읽었습니다. 이 어긋남은
    /// `doc/tool-findings.md` 에 적어 두었습니다.
    /// </remarks>
    public static class Battle
    {
        /// <summary>방어가 깎을 수 있는 최대이다. 이것이 없으면 고레벨에서 피해가 0이 된다.</summary>
        private const int MaxMitigation = 8000;

        public static BattleReport Run(IEnumerable<Combatant> party,
                                       IEnumerable<Combatant> enemies,
                                       int seed)
        {
            var report = new BattleReport();
            report.Party.AddRange(party);
            report.Enemies.AddRange(enemies);

            var rng = new Rng(seed);
            var all = report.Party.Concat(report.Enemies).ToList();

            foreach (var c in all)
                Prepare(c);

            int turn = 0;
            while (turn < BattleConst.MaxTurn)
            {
                // **순서는 회마다 다시 정합니다.** 느려짐이 실제로 순서를 바꾸게 하는 자리입니다.
                var order = all.Where(c => c.Alive)
                               .OrderByDescending(c => c.Stat(StatKind.Speed))
                               .ThenBy(c => BattleConst.SpeedTiebreak ? c.Placement : 0)
                               .ToList();

                foreach (var actor in order)
                {
                    if (!actor.Alive || turn >= BattleConst.MaxTurn)
                        continue;

                    turn++;
                    TakeTurn(report, actor, rng);

                    if (!report.Party.Any(c => c.Alive) || !report.Enemies.Any(c => c.Alive))
                    {
                        report.Turns = turn;
                        report.PartyWon = report.Party.Any(c => c.Alive);
                        report.Say(report.PartyWon ? "파티가 이겼습니다." : "파티가 졌습니다.");
                        return report;
                    }
                }
            }

            // 결착이 없으면 남은 체력 비율이 높은 쪽입니다.
            report.Turns = turn;
            report.DecidedByHealth = true;
            double ours = HealthRatio(report.Party);
            double theirs = HealthRatio(report.Enemies);
            report.PartyWon = ours >= theirs;
            report.Say($"{BattleConst.MaxTurn}턴에 결착이 없어 체력 비율로 판정합니다 — "
                           + $"{ours * 100:0.#}% 대 {theirs * 100:0.#}%.");
            return report;
        }

        private static double HealthRatio(List<Combatant> side)
        {
            long hp = side.Sum(c => (long)Math.Max(0, c.Hp));
            long max = side.Sum(c => (long)c.MaxHp);
            return max <= 0 ? 0.0 : hp / (double)max;
        }

        private static void Prepare(Combatant c)
        {
            c.Base = Stats.Compute(c.Monster, c.Level, c.Resonance);
            c.MaxHp = Numbers.Apply(c.Base.Hp, c.BossStatFactor);
            c.Hp = c.MaxHp;
            c.Cooldowns = new int[c.Active.Length];
            c.NextSlot = 0;
            c.Buffs.Clear();
            c.Statuses.Clear();

            // 패시브는 전투가 시작할 때 한 번 얹히고 사라지지 않습니다.
            foreach (var passive in c.Passive)
            {
                foreach (var effect in EffectsOf(passive))
                {
                    if (effect is BuffEffect buff)
                        c.Buffs.Add(new Combatant.Buff
                        {
                            Stat = buff.Stat,
                            Ratio = buff.Ratio,
                            Turns = int.MaxValue,
                        });
                }
            }
        }

        private static void TakeTurn(BattleReport report, Combatant actor, Rng rng)
        {
            // 지속 시간이 먼저 줄어듭니다.
            for (int i = actor.Buffs.Count - 1; i >= 0; i--)
            {
                var buff = actor.Buffs[i];
                if (buff.Turns == int.MaxValue)
                    continue;
                buff.Turns--;
                if (buff.Turns <= 0)
                    actor.Buffs.RemoveAt(i);
                else
                    actor.Buffs[i] = buff;
            }

            foreach (var kind in actor.Statuses.Keys.ToList())
            {
                if (--actor.Statuses[kind] <= 0)
                    actor.Statuses.Remove(kind);
            }

            // 화상은 자기 차례가 올 때마다 최대 체력의 일부를 깎습니다.
            if (actor.Statuses.ContainsKey(StatusKind.Burn))
            {
                int burn = Math.Max(1, actor.MaxHp / 20);
                actor.Hp = Math.Max(0, actor.Hp - burn);
                report.Say($"{Korean.Ga(actor.Name)} 화상으로 {burn} 을 잃었습니다.",
                           BeatKind.Damage, actor, actor, burn, note: "화상");
                if (!actor.Alive)
                {
                    report.Say($"{Korean.Ga(actor.Name)} 쓰러졌습니다.",
                               BeatKind.Down, null, actor);
                    return;
                }
            }

            if (actor.Statuses.ContainsKey(StatusKind.Stun))
            {
                report.Say($"{Korean.Ga(actor.Name)} 기절해 움직이지 못했습니다.",
                           BeatKind.Status, actor, actor, note: "기절!");
                return;
            }

            for (int i = 0; i < actor.Cooldowns.Length; i++)
            {
                if (actor.Cooldowns[i] > 0)
                    actor.Cooldowns[i]--;
            }

            int slot = PickSlot(actor);
            if (slot < 0)
            {
                report.Say($"{Korean.Ga(actor.Name)} 쓸 수 있는 스킬이 없어 쉬었습니다.",
                           BeatKind.Line, actor, actor, note: "패스");
                return;
            }

            var skill = actor.Active[slot];
            actor.Cooldowns[slot] = skill.Cooldown;
            actor.NextSlot = (slot + 1) % actor.Active.Length;

            var targets = Targets(report, actor, skill.TargetScope);
            if (targets.Count == 0)
                return;

            report.Say($"{actor.Name} — {skill.Name}", BeatKind.Act, actor, null,
                       note: skill.Name, icon: skill.Icon);

            int growth = SkillPowerFactor(skill, SkillLevelOf(actor, slot));
            foreach (var effect in EffectsOf(skill))
            {
                foreach (var target in targets.Where(t => t.Alive).ToList())
                    ApplyEffect(report, actor, target, skill, effect, growth, rng);
            }
        }

        /// <summary>
        /// 슬롯 순서대로 순환하고, 재사용 대기 중인 것은 건너뛴다.
        /// </summary>
        /// <remarks>
        /// **전부 대기 중이면 그 자리에서 가장 빨리 준비되는 것을 앞당깁니다.** 기획서 9.3 은
        /// 「대기 중인 것은 건너뜁니다」까지만 정했고 하나도 못 쓰는 경우를 다루지 않았습니다.
        /// 그대로 두면 대기가 긴 스킬만 가진 개체가 턴을 통째로 버리고, 화면에는 「쓸 수 있는
        /// 스킬이 없어 쉬었습니다」만 되풀이됩니다.
        ///
        /// 대기의 **순서는 그대로**입니다 — 남은 시간을 모두 같은 만큼 당기므로, 어느 것이
        /// 먼저 돌아오는지는 바뀌지 않습니다.
        /// </remarks>
        private static int PickSlot(Combatant actor)
        {
            if (actor.Active.Length == 0)
                return -1;

            for (int step = 0; step < actor.Active.Length; step++)
            {
                int slot = (actor.NextSlot + step) % actor.Active.Length;
                if (actor.Cooldowns[slot] <= 0)
                    return slot;
            }

            int soonest = actor.Cooldowns.Min();
            for (int i = 0; i < actor.Cooldowns.Length; i++)
                actor.Cooldowns[i] -= soonest;

            for (int step = 0; step < actor.Active.Length; step++)
            {
                int slot = (actor.NextSlot + step) % actor.Active.Length;
                if (actor.Cooldowns[slot] <= 0)
                    return slot;
            }
            return -1;
        }

        private static List<Combatant> Targets(BattleReport report, Combatant actor,
                                               TargetScope scope)
        {
            var foes = (actor.IsEnemy ? report.Party : report.Enemies).Where(c => c.Alive).ToList();
            var friends = (actor.IsEnemy ? report.Enemies : report.Party).Where(c => c.Alive).ToList();

            return scope switch
            {
                TargetScope.Single => Lowest(foes),
                TargetScope.AllEnemy => foes,
                TargetScope.OneAlly => Lowest(friends),
                _ => friends,
            };

            // 단일 대상은 체력이 가장 적은 쪽입니다. 입력이 없으므로 규칙이 정해져 있어야 합니다.
            static List<Combatant> Lowest(List<Combatant> side)
            {
                if (side.Count == 0)
                    return side;
                var pick = side.OrderBy(c => c.MaxHp <= 0 ? 1.0 : c.Hp / (double)c.MaxHp)
                               .ThenBy(c => c.Placement)
                               .First();
                return new List<Combatant> { pick };
            }
        }

        private static void ApplyEffect(BattleReport report, Combatant actor, Combatant target,
                                        SkillRecord skill, Effect effect,
                                        int growth, Rng rng)
        {
            if (!rng.Chance(effect.Chance))
                return;

            switch (effect)
            {
                case DamageEffect damage:
                {
                    if (actor.Statuses.ContainsKey(StatusKind.Blind) && rng.Chance(5000))
                    {
                        report.Say($"  {Korean.Eul(target.Name)} 빗맞혔습니다.",
                                   BeatKind.Miss, actor, target);
                        return;
                    }

                    int raw = Numbers.Apply(actor.Stat(StatKind.Attack),
                                            Numbers.Apply(damage.Power, growth));

                    int mitigation = Numbers.Clamp(
                        target.Stat(StatKind.Defense) * BattleConst.DefenseFactor,
                        0, MaxMitigation);
                    raw = Numbers.Apply(raw, Numbers.One - mitigation);

                    int affinity = Affinity(skill, actor, target);
                    raw = Numbers.Apply(raw, affinity);

                    bool crit = rng.Chance(actor.Stat(StatKind.CritRate));
                    if (crit)
                        raw = Numbers.Apply(raw, actor.Base.CritPower);

                    raw = Math.Max(1, raw);
                    target.Hp = Math.Max(0, target.Hp - raw);
                    report.Say($"  {target.Name}에게 {raw}{(crit ? " (치명)" : "")}"
                               + AffinityTag(affinity),
                               BeatKind.Damage, actor, target, raw, crit,
                               affinity: affinity);

                    if (!target.Alive)
                        report.Say($"  {Korean.Ga(target.Name)} 쓰러졌습니다.",
                                   BeatKind.Down, actor, target);
                    break;
                }

                case HealEffect heal:
                {
                    int amount = Numbers.Apply(actor.Stat(StatKind.Attack),
                                               Numbers.Apply(heal.Power, growth));
                    int before = target.Hp;
                    target.Hp = Math.Min(target.MaxHp, target.Hp + amount);
                    report.Say($"  {Korean.Ga(target.Name)} {target.Hp - before} 회복했습니다.",
                               BeatKind.Heal, actor, target, target.Hp - before);
                    break;
                }

                case BuffEffect buff:
                {
                    target.Buffs.Add(new Combatant.Buff
                    {
                        Stat = buff.Stat,
                        Ratio = buff.Ratio,
                        Turns = buff.Duration,
                    });
                    report.Say($"  {Korean.Ui(target.Name)} {Label(buff.Stat)} "
                               + $"{(buff.Ratio >= 0 ? "+" : "")}{Numbers.AsPercent(buff.Ratio)} "
                               + $"{buff.Duration}턴",
                               BeatKind.Buff, actor, target, buff.Ratio);
                    break;
                }

                case StatusEffect status:
                {
                    target.Statuses[status.Status] = status.Duration;
                    report.Say($"  {Korean.Ga(target.Name)} {Label(status.Status)} 되었습니다.",
                               BeatKind.Status, actor, target);
                    break;
                }
            }
        }

        /// <summary>「유리」와 「불리」를 글로 적는다. 없으면 빈 문자열이다.</summary>
        public static string AffinityTag(int affinity)
        {
            if (affinity > BattleConst.NeutralAffinity)
                return $"  유리 {Numbers.AsMultiplier(affinity)}";
            if (affinity > 0 && affinity < BattleConst.NeutralAffinity)
                return $"  불리 {Numbers.AsMultiplier(affinity)}";
            return "";
        }

        /// <summary>속성이 없는 스킬은 상성을 타지 않는다.</summary>
        private static int Affinity(SkillRecord skill, Combatant actor, Combatant target)
        {
            if (!skill.HasElement)
                return BattleConst.NeutralAffinity;

            var row = WildlingData.ElementAffinity
                .FindByAttackerAndDefender(skill.Element, target.Monster.Element);
            return row?.Factor ?? BattleConst.NeutralAffinity;
        }

        private static int SkillLevelOf(Combatant actor, int slot)
            => slot < actor.SkillLevels.Length ? Math.Max(1, actor.SkillLevels[slot]) : 1;

        private static int SkillPowerFactor(SkillRecord skill, int level)
            => WildlingData.SkillGrowth.Records
                   .FirstOrDefault(r => r.SkillId == skill.SkillId && r.Level == level)
                   ?.PowerFactor ?? Numbers.One;

        /// <summary>스킬 하나가 일으키는 효과를 순서대로 낸다.</summary>
        public static IEnumerable<Effect> EffectsOf(SkillRecord skill)
            => WildlingData.SkillEffect.Records
                .Where(r => r.SkillId == skill.SkillId)
                .OrderBy(r => r.Order)
                .Select(r => r.Effect);

        public static string Label(StatKind kind) => kind switch
        {
            StatKind.Attack => "공격",
            StatKind.Defense => "방어",
            StatKind.Speed => "속도",
            _ => "치명",
        };

        public static string Label(StatusKind kind) => kind switch
        {
            StatusKind.Stun => "기절",
            StatusKind.Slow => "둔화",
            StatusKind.Blind => "실명",
            _ => "화상",
        };
    }
}
