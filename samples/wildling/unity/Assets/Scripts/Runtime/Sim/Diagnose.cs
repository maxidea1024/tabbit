using System.Collections.Generic;
using System.Linq;
using Wildling.Data;

namespace Wildling.Game
{
    /// <summary>
    /// 왜 졌는지 기록에서 읽어 낸다.
    /// </summary>
    /// <remarks>
    /// **패배가 정보가 되어야 판단이 생깁니다.** 「졌습니다」만 나오면 다음에 무엇을 바꿔야
    /// 할지 알 수 없고, 그러면 편성은 판단이 아니라 우연이 됩니다.
    ///
    /// 진단은 전부 `BattleReport` 만 봅니다 — 표를 다시 읽지 않으므로 계산과 어긋날 자리가
    /// 없습니다.
    /// </remarks>
    public static class Diagnose
    {
        public static List<string> Why(BattleReport battle)
        {
            var reasons = new List<string>();

            // 1. 상성이 불리했는가.
            var beats = battle.Beats.Where(b => b.Kind == BeatKind.Damage && b.Affinity > 0)
                                    .ToList();
            int weak = beats.Count(b => !b.TargetIsEnemy && b.Affinity < BattleConst.NeutralAffinity);
            int wasted = beats.Count(b => b.TargetIsEnemy && b.Affinity < BattleConst.NeutralAffinity);

            var enemyElements = battle.Enemies.Select(c => c.Monster.Element).Distinct().ToList();
            if (wasted > beats.Count / 3 && wasted > 0)
            {
                reasons.Add($"우리 공격이 불리한 상성으로 들어갔습니다 — 상대가 "
                            + $"{string.Join(" · ", enemyElements.Select(Theme.Label))} 입니다. "
                            + "유리한 속성으로 편성을 바꾸십시오.");
            }
            if (weak > 0)
                reasons.Add($"상대의 유리한 속성에 {weak}번 맞았습니다.");

            // 2. 회복이 있었는가.
            bool healed = battle.Beats.Any(b => b.Kind == BeatKind.Heal && !b.TargetIsEnemy);
            if (!healed && battle.Party.Any(c => !c.Alive))
                reasons.Add("회복이 한 번도 없었습니다 — 수호 역할을 파티에 넣으십시오.");

            // 3. 30턴 판정으로 갈렸는가.
            if (battle.DecidedByHealth)
            {
                reasons.Add($"{BattleConst.MaxTurn}턴 안에 끝내지 못해 체력 비율로 갈렸습니다 — "
                            + "공격이 모자랍니다.");
            }

            // 4. 그냥 약한가.
            long ourAttack = battle.Party.Sum(c => (long)c.Base.Attack);
            long theirHp = battle.Enemies.Sum(c => (long)c.MaxHp);
            if (ourAttack * BattleConst.MaxTurn < theirHp)
            {
                reasons.Add($"우리 공격 합 {ourAttack} 으로 상대 체력 합 {theirHp} 을 "
                            + $"{BattleConst.MaxTurn}턴 안에 깎을 수 없습니다 — 레벨을 올리거나 "
                            + "각성하십시오.");
            }

            // 5. 상태 이상에 묶였는가.
            int stunned = battle.Beats.Count(
                b => b.Kind == BeatKind.Status && !b.TargetIsEnemy && b.Note == "기절!");
            if (stunned >= 2)
                reasons.Add($"{stunned}번 기절해 그만큼 턴을 잃었습니다.");

            if (reasons.Count == 0)
                reasons.Add("한 끗 차이였습니다. 레벨을 조금 올리고 다시 시도하십시오.");

            return reasons;
        }

        /// <summary>
        /// 편성의 전력이다.
        /// </summary>
        /// <remarks>
        /// **정확한 승패 예측이 아니라 견줄 수 있는 하나의 수**입니다. 체력과 공격이 함께
        /// 들어가야 한쪽만 올린 편성이 세 보이지 않습니다.
        /// </remarks>
        public static long Power(StatBlock s)
            => s.Hp / 6 + s.Attack * 8L + s.Defense * 5L + s.Speed * 2L;

        public static long PartyPower(GameState state, IEnumerable<Owned> members)
            => members.Where(o => o?.Row != null)
                      .Sum(o => Power(Stats.Compute(o.Row, o.Level,
                                                    state.Resonance(o.SpeciesId))));

        public static long StagePower(StageRecord stage)
        {
            if (stage is null)
                return 0;

            long total = 0;
            var wave = stage.MonsterByWaveMonsterIds
                       ?? System.Array.Empty<MonsterRecord>();

            for (int i = 0; i < wave.Length; i++)
            {
                if (wave[i] is null)
                    continue;
                int level = i < stage.WaveLevels.Length ? stage.WaveLevels[i] : 1;
                total += Power(Stats.Compute(wave[i], level, 0));
            }
            return total;
        }

        /// <summary>전력 차이를 사람이 읽는 말로 만든다.</summary>
        public static (string Text, bool Good) Compare(long ours, long theirs)
        {
            if (theirs <= 0)
                return ("비교할 상대가 없습니다.", true);

            int percent = (int)(ours * 100 / theirs);
            if (percent >= 140)
                return ($"전력 {percent}% — 넉넉합니다.", true);
            if (percent >= 100)
                return ($"전력 {percent}% — 해 볼 만합니다.", true);
            if (percent >= 75)
                return ($"전력 {percent}% — 아슬아슬합니다.", false);
            return ($"전력 {percent}% — 모자랍니다. 키우고 오십시오.", false);
        }
    }
}
