using System.Text;
using Wildling.Data;

namespace Wildling.Game
{
    /// <summary>
    /// 전투 기록 한 줄을 색이 있는 글로 만든다.
    /// </summary>
    /// <remarks>
    /// **색은 화면 쪽에만 있습니다.** `Battle` 이 남기는 글은 색이 없는 그대로이고 —
    /// `unity-play.txt` 같은 보고에 그대로 들어가야 합니다 — 이 파일이 그 위에 색을 얹습니다.
    ///
    /// 레거시 `UnityEngine.UI.Text` 의 리치 텍스트를 씁니다. `&lt;color&gt;` 와 `&lt;b&gt;`
    /// 두 가지면 이름과 수치를 가르는 데 충분합니다.
    /// </remarks>
    public static class BattleLog
    {
        private const string Ally = "#8FD8A0";     // 우리 편 이름
        private const string Foe = "#E8907F";      // 상대 이름
        private const string Hit = "#FF7A5C";      // 피해
        private const string Crit = "#FFD24A";     // 치명타
        private const string Mend = "#7ADF8C";     // 회복
        private const string Skill = "#CFC2F2";    // 스킬 이름
        private const string Strong = "#FFA63C";   // 유리
        private const string Weak = "#9BB4DC";     // 불리
        private const string Plain = "#9AA0B4";    // 나머지

        /// <summary>그 박자를 색이 있는 한 줄로 만든다.</summary>
        public static string Rich(BattleReport report, BattleReport.Beat beat)
        {
            string actor = Wrap(Name(report, beat.ActorIsEnemy, beat.ActorIndex),
                                beat.ActorIsEnemy ? Foe : Ally);
            string target = Wrap(Name(report, beat.TargetIsEnemy, beat.TargetIndex),
                                 beat.TargetIsEnemy ? Foe : Ally);

            switch (beat.Kind)
            {
                case BeatKind.Act:
                    return $"{actor}  {Wrap(beat.Note, Skill, bold: true)}";

                case BeatKind.Damage:
                {
                    var sb = new StringBuilder("  ");
                    sb.Append(target).Append("에게 ");
                    sb.Append(Wrap(beat.Amount.ToString(), beat.Crit ? Crit : Hit, bold: true));
                    if (beat.Crit)
                        sb.Append(Wrap(" 치명", Crit));
                    sb.Append(Affinity(beat.Affinity));
                    if (!string.IsNullOrEmpty(beat.Note))
                        sb.Append(Wrap($" ({beat.Note})", Plain));
                    return sb.ToString();
                }

                case BeatKind.Heal:
                    return $"  {target} {Wrap("+" + beat.Amount, Mend, bold: true)} 회복";

                case BeatKind.Down:
                    return $"  {target} {Wrap("쓰러졌습니다", Hit, bold: true)}";

                case BeatKind.Miss:
                    return $"  {target} {Wrap("빗맞음", Plain)}";

                default:
                    // 변동과 상태는 문장이 길므로 이름만 물들이고 나머지는 그대로 둡니다.
                    return Recolor(report, beat);
            }
        }

        /// <summary>이름만 찾아 물들인다.</summary>
        private static string Recolor(BattleReport report, BattleReport.Beat beat)
        {
            string text = beat.Text ?? "";

            foreach (var c in report.Enemies)
                text = text.Replace(c.Name, Wrap(c.Name, Foe));
            foreach (var c in report.Party)
                text = text.Replace(c.Name, Wrap(c.Name, Ally));

            if (!string.IsNullOrEmpty(beat.Note) && beat.Kind == BeatKind.Status)
                text = text.Replace(beat.Note, Wrap(beat.Note, Crit, bold: true));

            return text;
        }

        private static string Affinity(int factor)
        {
            if (factor <= 0 || factor == BattleConst.NeutralAffinity)
                return "";
            return factor > BattleConst.NeutralAffinity
                ? Wrap($"  유리 {Numbers.AsMultiplier(factor)}", Strong)
                : Wrap($"  불리 {Numbers.AsMultiplier(factor)}", Weak);
        }

        private static string Name(BattleReport report, bool isEnemy, int index)
        {
            var side = isEnemy ? report.Enemies : report.Party;
            return index >= 0 && index < side.Count ? side[index].Name : "";
        }

        private static string Wrap(string text, string color, bool bold = false)
        {
            if (string.IsNullOrEmpty(text))
                return "";
            string body = bold ? $"<b>{text}</b>" : text;
            return $"<color={color}>{body}</color>";
        }
    }
}
