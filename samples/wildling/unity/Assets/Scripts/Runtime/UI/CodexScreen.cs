using System.Linq;
using UnityEngine;
using Wildling.Data;

namespace Wildling.Game
{
    /// <summary>
    /// 생태 기록부이다.
    /// </summary>
    /// <remarks>
    /// **`Monster` 54행이 전부 나옵니다.** 미기록 종도 자리를 가지고, 이름과 설명이 상태에
    /// 따라 가려집니다 — 기획서 6.3 의 표 그대로입니다.
    /// </remarks>
    public sealed class CodexScreen : Screen
    {
        public override string Title => "생태 기록부";

        public override void Build(Transform root, App app)
        {
            var state = app.State;
            var column = Ui.Scroll(root);

            var head = Ui.Item(column, 84f);
            Ui.Panel(head.transform, Theme.Panel);
            var inner = Ui.Column(head.transform, 6f, 12f);

            int completion = state.Completion();
            var line = Ui.Item(inner, 30f);
            Ui.Label(line.transform,
                     $"완성률 {Numbers.AsPercent(completion)} · "
                     + $"정독 {WildlingData.Monster.Records.Count(r => state.CodexState(r.MonsterId) == CodexState.Studied)}행",
                     22);
            var bar = Ui.Item(inner, 14f);
            Ui.Bar(bar.transform, completion / (float)Numbers.One, Theme.Accent);

            // 받을 수 있는 완성 보상이 있으면 여기서 받습니다.
            var pending = state.PendingCodexRewards();
            if (pending.Count > 0)
            {
                App.Section(column, "받을 수 있는 완성 보상");
                foreach (var reward in pending)
                {
                    var item = Ui.Item(column, 70f);
                    string where = reward.HasRegionId
                        ? reward.RegionByRegionId?.Name ?? reward.RegionId
                        : "전체";
                    Ui.Button(item.transform, $"{where} {reward.Threshold}% 보상 받기", () =>
                    {
                        var grants = Rewards.Certain(reward.RewardGroupId);
                        var report = state.Apply(grants);
                        state.MarkCodexClaimed(reward.CodexRewardId);
                        SaveStore.Save(state);
                        app.Toast(report.Lines.Count > 0
                            ? string.Join(" · ", report.Lines)
                            : "받았습니다.");
                        app.Rebuild();
                    }, Theme.Accent);
                }
            }

            foreach (var region in WildlingData.Region.Records.OrderBy(r => r.Order))
            {
                long mask = 1L << (region.Order - 1);
                var rows = WildlingData.Monster.Records
                    .Where(r => (r.Habitat & mask) != 0)
                    .OrderBy(r => r.SpeciesId)
                    .ThenBy(r => r.Stage)
                    .ToList();

                if (rows.Count == 0)
                    continue;

                int recorded = rows.Count(r => state.CodexState(r.MonsterId) >= CodexState.Recorded);
                App.Section(column, $"{region.Name} — {recorded} / {rows.Count}");

                foreach (var row in rows)
                    Entry(column, app, row);
            }
        }

        private static void Entry(Transform column, App app, MonsterTable.Record row)
        {
            var state = app.State;
            var codex = state.CodexState(row.MonsterId);

            if (codex == CodexState.Unknown)
            {
                // 미기록은 실루엣입니다. 속성과 등급만 보입니다.
                var item = Ui.Item(column, 112f);
                Ui.Panel(item.transform, Theme.Panel);

                var icon = Ui.Node("icon", item.transform);
                var irt = Ui.Rect(icon);
                irt.anchorMin = new Vector2(0f, 0.5f);
                irt.anchorMax = new Vector2(0f, 0.5f);
                irt.pivot = new Vector2(0f, 0.5f);
                irt.sizeDelta = new Vector2(96f, 96f);
                irt.anchoredPosition = new Vector2(8f, 0f);
                var image = Ui.Icon(icon.transform, ArtLibrary.Icon(row.Icon));
                image.color = new Color(0.10f, 0.10f, 0.14f, 0.92f);

                var text = Ui.Node("text", item.transform);
                var trt = Ui.Stretch(text);
                trt.offsetMin = new Vector2(112f, 0f);
                trt.offsetMax = new Vector2(-12f, 0f);
                Ui.Label(text.transform,
                         $"???\n{Theme.Label(row.Element)} · {Theme.Label(row.Grade)}",
                         22, Theme.TextDim);
                return;
            }

            int observed = state.Observed(row.MonsterId);
            int cap = GameState.ObserveCap(row.Grade);
            string right = codex switch
            {
                CodexState.Sighted => "목격",
                CodexState.Recorded => $"관측 {observed}/{cap}",
                _ => "정독",
            };

            App.MonsterRow(column, row, right, () => app.Go(new CodexEntryScreen(row.MonsterId)));
        }
    }

    /// <summary>기록부의 한 행이다.</summary>
    public sealed class CodexEntryScreen : Screen
    {
        private readonly string _monsterId;

        public CodexEntryScreen(string monsterId) => _monsterId = monsterId;

        public override string Title => "기록";

        public override void Build(Transform root, App app)
        {
            var state = app.State;
            var row = WildlingData.Monster.FindByMonsterId(_monsterId);
            if (row is null)
                return;

            var column = Ui.Scroll(root);
            var codex = state.CodexState(_monsterId);

            var head = Ui.Item(column, 220f);
            Ui.Panel(head.transform, Theme.Panel);

            var icon = Ui.Node("icon", head.transform);
            var irt = Ui.Rect(icon);
            irt.anchorMin = new Vector2(0f, 0.5f);
            irt.anchorMax = new Vector2(0f, 0.5f);
            irt.pivot = new Vector2(0f, 0.5f);
            irt.sizeDelta = new Vector2(180f, 180f);
            irt.anchoredPosition = new Vector2(14f, 0f);
            Ui.Icon(icon.transform, ArtLibrary.Icon(row.Icon));

            var text = Ui.Node("text", head.transform);
            var trt = Ui.Stretch(text);
            trt.offsetMin = new Vector2(206f, 14f);
            trt.offsetMax = new Vector2(-14f, -14f);
            var lines = Ui.Column(text.transform, 6f);

            var name = Ui.Item(lines, 40f);
            Ui.Label(name.transform, $"{row.Name}  ({row.DisplayCode})", 28);

            var tags = Ui.Item(lines, 30f);
            Ui.Label(tags.transform,
                     $"{Theme.Label(row.Element)} · {Theme.Label(row.Grade)} · "
                     + $"{Theme.Label(row.Role)} · {row.Stage}/{row.MaxStage}단",
                     20, Theme.TextDim);

            var desc = Ui.Item(lines, 70f);
            Ui.Label(desc.transform,
                     codex >= CodexState.Recorded ? row.Description : "기록되지 않았습니다.",
                     20, Theme.TextDim, TextAnchor.UpperLeft);

            // 서식지는 `bitset` 하나가 여러 지역을 가리킵니다.
            App.Section(column, "서식");
            var habitat = Ui.Item(column, 50f);
            var where = WildlingData.Region.Records
                .Where(r => (row.Habitat & (1L << (r.Order - 1))) != 0)
                .Select(r => r.Name)
                .ToList();
            Ui.Label(habitat.transform,
                     codex >= CodexState.Studied
                         ? (where.Count > 0 ? string.Join(" · ", where) : "알려지지 않았습니다.")
                         : "정독하면 열립니다.",
                     22, codex >= CodexState.Studied ? Theme.Text : Theme.TextDim);

            // 셀 배열이 원소 여럿으로 옵니다.
            if (row.Tags is { Length: > 0 })
            {
                App.Section(column, "태그");
                var tagItem = Ui.Item(column, 44f);
                Ui.Label(tagItem.transform, string.Join(" · ", row.Tags), 20, Theme.TextDim);
            }

            App.Section(column, "기본 능력치");
            var stats = Ui.Item(column, 60f);
            Ui.Panel(stats.transform, Theme.Panel);
            var b = row.Base;
            var statText = Ui.Node("t", stats.transform);
            var srt = Ui.Stretch(statText);
            srt.offsetMin = new Vector2(12f, 0f);
            srt.offsetMax = new Vector2(-12f, 0f);
            Ui.Label(statText.transform,
                     $"체력 {b.Hp} · 공격 {b.Attack} · 방어 {b.Defense} · 속도 {b.Speed} · "
                     + $"치명 {Numbers.AsPercent(b.CritRate)}",
                     20);

            App.Section(column, "쓸 수 있는 스킬");
            foreach (var skill in WildlingData.MonsterSkill.Records
                         .Where(r => r.MonsterId == _monsterId)
                         .OrderBy(r => r.UnlockStage))
            {
                SkillRow(column, skill.SkillBySkillId, skill.SlotKind, skill.UnlockStage);
            }

            var owned = state.All.FirstOrDefault(o => o.MonsterId == _monsterId);
            if (owned != null)
            {
                var button = Ui.Item(column, 70f);
                Ui.Button(button.transform, "이 개체 보기",
                          () => app.Go(new MonsterScreen(owned.Uid)), Theme.Accent);
            }
        }

        /// <summary>스킬 한 줄이다. 효과는 다형이므로 변종마다 다르게 적힌다.</summary>
        public static void SkillRow(Transform column, SkillTable.Record skill,
                                    SlotKind slot, int unlockStage)
        {
            if (skill is null)
                return;

            var item = Ui.Item(column, 104f);
            Ui.Panel(item.transform, Theme.Panel);

            var icon = Ui.Node("icon", item.transform);
            var irt = Ui.Rect(icon);
            irt.anchorMin = new Vector2(0f, 0.5f);
            irt.anchorMax = new Vector2(0f, 0.5f);
            irt.pivot = new Vector2(0f, 0.5f);
            irt.sizeDelta = new Vector2(84f, 84f);
            irt.anchoredPosition = new Vector2(8f, 0f);
            Ui.Icon(icon.transform, ArtLibrary.Icon(skill.Icon));

            var text = Ui.Node("text", item.transform);
            var trt = Ui.Stretch(text);
            trt.offsetMin = new Vector2(100f, 8f);
            trt.offsetMax = new Vector2(-12f, -8f);
            var lines = Ui.Column(text.transform, 2f);

            var name = Ui.Item(lines, 32f);
            Ui.Label(name.transform,
                     $"{skill.Name}   {(slot == SlotKind.Active ? "액티브" : "패시브")}"
                     + $"   {unlockStage}단부터", 24);

            var scope = Ui.Item(lines, 26f);
            Ui.Label(scope.transform,
                     $"{Theme.Label(skill.TargetScope)} · 재사용 {skill.Cooldown}턴"
                     + (skill.HasElement ? $" · {Theme.Label(skill.Element)}" : " · 무속성"),
                     19, Theme.TextDim);

            var effects = Ui.Item(lines, 26f);
            Ui.Label(effects.transform, DescribeEffects(skill), 19, Theme.Accent);
        }

        /// <summary>
        /// 효과 목록을 사람이 읽는 한 줄로 만든다.
        /// </summary>
        /// <remarks>
        /// **판별자로 좁혀 변종마다 다른 컬럼을 읽습니다.** 피해는 배수를, 회복은 회복량을,
        /// 상태 부여는 대상 상태와 지속 턴을 듭니다 — 기획서 9.3 이 「다형 레코드가 가장
        /// 강하게 요구되는 자리」라고 적은 곳입니다.
        /// </remarks>
        public static string DescribeEffects(SkillTable.Record skill)
        {
            var parts = Battle.EffectsOf(skill).Select(effect => effect switch
            {
                DamageEffect damage => $"피해 {Numbers.AsMultiplier(damage.Power)}",
                HealEffect heal => $"회복 {Numbers.AsMultiplier(heal.Power)}",
                BuffEffect buff =>
                    $"{Battle.Label(buff.Stat)} {(buff.Ratio >= 0 ? "+" : "")}"
                    + $"{Numbers.AsPercent(buff.Ratio)} {buff.Duration}턴",
                StatusEffect status =>
                    $"{Battle.Label(status.Status)} {Numbers.AsPercent(status.Chance)} "
                    + $"{status.Duration}턴",
                _ => "?",
            });
            return string.Join(" · ", parts);
        }
    }
}
