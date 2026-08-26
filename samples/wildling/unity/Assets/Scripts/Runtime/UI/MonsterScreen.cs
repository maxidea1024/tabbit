using System.Linq;
using UnityEngine;
using Wildling.Data;

namespace Wildling.Game
{
    /// <summary>
    /// 동행 개체 하나이다. 육성 · 공명 · 각성이 여기 있다.
    /// </summary>
    /// <remarks>
    /// **표 다섯이 한 화면에서 만납니다** — `GrowthCurve` 가 다음 레벨의 값과 비용을,
    /// `ResonanceRank` 가 조각 비용을, `MonsterAwakening` 이 각성 관계와 재료를,
    /// `RequirementGroup` 이 조건을, `MonsterSkill` 이 슬롯 후보를 냅니다.
    /// </remarks>
    public sealed class MonsterScreen : Screen
    {
        private readonly int _uid;

        public MonsterScreen(int uid) => _uid = uid;

        public override string Title => "육성";

        public override void Build(Transform root, App app)
        {
            var state = app.State;
            var owned = state.Find(_uid);
            var row = owned?.Row;
            if (row is null)
            {
                Ui.Label(root, "그 개체가 없습니다.", 24, Theme.TextDim, TextAnchor.MiddleCenter);
                return;
            }

            int resonance = state.Resonance(owned.SpeciesId);
            var stats = Stats.Compute(row, owned.Level, resonance);
            var column = Ui.Scroll(root);

            // ---------------------------------------------------------- 머리
            var head = Ui.Item(column, 230f);
            Ui.Panel(head.transform, Theme.Panel);

            var icon = Ui.Node("icon", head.transform);
            var irt = Ui.Rect(icon);
            irt.anchorMin = new Vector2(0f, 0.5f);
            irt.anchorMax = new Vector2(0f, 0.5f);
            irt.pivot = new Vector2(0f, 0.5f);
            irt.sizeDelta = new Vector2(190f, 190f);
            irt.anchoredPosition = new Vector2(14f, 0f);
            Ui.Icon(icon.transform, ArtLibrary.Icon(row.Icon));

            var text = Ui.Node("text", head.transform);
            var trt = Ui.Stretch(text);
            trt.offsetMin = new Vector2(216f, 12f);
            trt.offsetMax = new Vector2(-14f, -12f);
            var lines = Ui.Column(text.transform, 4f);

            var name = Ui.Item(lines, 38f);
            Ui.Label(name.transform, row.Name, 28);

            var tags = Ui.Item(lines, 28f);
            Ui.Label(tags.transform,
                     $"{Theme.Label(row.Element)} · {Theme.Label(row.Grade)} · "
                     + $"{Theme.Label(row.Role)} · {row.Stage}/{row.MaxStage}단",
                     20, Theme.TextDim);

            var level = Ui.Item(lines, 30f);
            Ui.Label(level.transform,
                     $"Lv {owned.Level} / {state.LevelCap(owned)}"
                     + (resonance > 0 ? $"   공명 {resonance}" : ""), 24, Theme.Accent);

            var statLine = Ui.Item(lines, 28f);
            Ui.Label(statLine.transform,
                     $"체력 {stats.Hp} · 공격 {stats.Attack} · 방어 {stats.Defense} · "
                     + $"속도 {stats.Speed}", 20);

            var bar = Ui.Item(lines, 12f);
            Ui.Bar(bar.transform, owned.Level / (float)state.LevelCap(owned), Theme.Accent);

            // ---------------------------------------------------------- 레벨
            App.Section(column, "레벨");
            BuildLevel(column, app, owned, stats);

            // ---------------------------------------------------------- 공명
            App.Section(column, "공명 등급");
            BuildResonance(column, app, owned, resonance);

            // ---------------------------------------------------------- 각성
            App.Section(column, "각성");
            BuildAwakening(column, app, owned);

            // ---------------------------------------------------------- 스킬
            App.Section(column, $"스킬 슬롯 — 액티브 {Stats.ActiveSlots(row.Stage)} · "
                                + $"패시브 {Stats.PassiveSlots(row.Stage)}");
            BuildSkills(column, app, owned);
        }

        // ------------------------------------------------------------ 레벨

        private static void BuildLevel(Transform column, App app, Owned owned, StatBlock now)
        {
            var state = app.State;
            int cap = state.LevelCap(owned);

            if (owned.Level >= cap)
            {
                var maxed = Ui.Item(column, 60f);
                Ui.Label(maxed.transform,
                         $"이 단계의 상한 {cap} 입니다. 각성해야 더 오릅니다.", 22, Theme.Warn);
                return;
            }

            var next = Stats.Compute(owned.Row, owned.Level + 1, state.Resonance(owned.SpeciesId));
            var costs = Stats.LevelCost(owned.Row.Grade, owned.Level + 1);

            var card = Ui.Item(column, 150f);
            Ui.Panel(card.transform, Theme.Panel);
            var inner = Ui.Column(card.transform, 6f, 12f);

            var delta = Ui.Item(inner, 30f);
            Ui.Label(delta.transform,
                     $"다음 레벨 — 체력 +{next.Hp - now.Hp} · 공격 +{next.Attack - now.Attack} "
                     + $"· 방어 +{next.Defense - now.Defense}", 21, Theme.Accent);

            var cost = Ui.Item(inner, 28f);
            Ui.Label(cost.transform,
                     "비용 — " + string.Join(" · ", costs.Select(c =>
                         $"{WildlingData.Currency.FindByCurrencyId(c.CurrencyId)?.Name ?? c.CurrencyId}"
                         + $" {c.Amount}")),
                     20, Theme.TextDim);

            var buttons = Ui.Item(inner, 58f);
            var row = Ui.Row(buttons.transform, 8f);

            bool can = state.CanLevelUp(owned);
            var one = Ui.Button(row, "레벨 올리기", () =>
            {
                state.LevelUp(owned);
                SaveStore.Save(state);
                app.Rebuild();
            }, can ? Theme.Accent : Theme.PanelHigh);
            one.interactable = can;

            var ten = Ui.Button(row, "가능한 만큼", () =>
            {
                int gained = 0;
                while (state.CanLevelUp(owned) && gained < 100)
                {
                    state.LevelUp(owned);
                    gained++;
                }
                SaveStore.Save(state);
                app.Toast(gained > 0 ? $"{gained} 레벨 올랐습니다." : "재화가 모자랍니다.");
                app.Rebuild();
            });
            ten.interactable = can;
        }

        // ------------------------------------------------------------ 공명

        private static void BuildResonance(Transform column, App app, Owned owned, int resonance)
        {
            var state = app.State;
            int shards = state.Shards(owned.SpeciesId);

            var card = Ui.Item(column, 146f);
            Ui.Panel(card.transform, Theme.Panel);
            var inner = Ui.Column(card.transform, 6f, 12f);

            var have = Ui.Item(inner, 30f);
            Ui.Label(have.transform,
                     $"울림 조각 {shards} · 공명 {resonance} / {GrowthConst.ResonanceCap}", 22);

            if (resonance >= GrowthConst.ResonanceCap)
            {
                var maxed = Ui.Item(inner, 30f);
                Ui.Label(maxed.transform, "공명이 상한입니다.", 20, Theme.Warn);
                return;
            }

            var rank = WildlingData.ResonanceRank
                .FindByGradeAndRank(owned.Row.Grade, resonance + 1);
            var note = Ui.Item(inner, 28f);
            Ui.Label(note.transform,
                     rank is null
                         ? "다음 등급이 표에 없습니다."
                         : $"다음 등급 — 능력치 {Numbers.AsMultiplier(rank.StatFactor)} · "
                           + $"조각 {rank.ShardCost}"
                           + (rank.HasUnlockNote ? $" · {rank.UnlockNote}" : ""),
                     20, Theme.TextDim);

            var buttons = Ui.Item(inner, 54f);
            bool can = state.CanResonanceUp(owned.SpeciesId);
            var button = Ui.Button(buttons.transform, "공명 올리기", () =>
            {
                state.ResonanceUp(owned.SpeciesId);
                SaveStore.Save(state);
                app.Rebuild();
            }, can ? Theme.Accent : Theme.PanelHigh);
            button.interactable = can;
        }

        // ------------------------------------------------------------ 각성

        private static void BuildAwakening(Transform column, App app, Owned owned)
        {
            var state = app.State;
            var link = state.AwakeningOf(owned);

            if (link is null)
            {
                var none = Ui.Item(column, 54f);
                Ui.Label(none.transform, "이 단계에서 더 각성하지 않습니다.", 22, Theme.TextDim);
                return;
            }

            // **참조가 낸 것은 키가 아니라 행입니다.** 다음 단계의 이름을 여기서 읽습니다.
            var to = link.MonsterByToMonsterId;
            bool can = state.CanAwaken(owned, out var checks);

            var card = Ui.Item(column, 120f + checks.Count * 34f);
            Ui.Panel(card.transform, Theme.Panel);
            var inner = Ui.Column(card.transform, 4f, 12f);

            var head = Ui.Item(inner, 34f);
            Ui.Label(head.transform,
                     $"{owned.Name} → {to?.Name ?? link.ToMonsterId}"
                     + (to != null ? $"  ({to.Stage}단)" : ""), 24);

            var gain = Ui.Item(inner, 28f);
            Ui.Label(gain.transform,
                     $"체력 +{link.Gain.Hp} · 공격 +{link.Gain.Attack} · 방어 +{link.Gain.Defense}",
                     20, Theme.Accent);

            foreach (var check in checks)
            {
                var line = Ui.Item(inner, 30f);
                Ui.Label(line.transform,
                         (check.Met ? "충족  " : "부족  ") + check.Text,
                         20, check.Met ? Theme.Good : Theme.Warn);
            }

            var buttons = Ui.Item(inner, 56f);
            var button = Ui.Button(buttons.transform, "각성", () =>
            {
                if (!state.Awaken(owned))
                {
                    app.Toast("조건이 모자랍니다.");
                    return;
                }
                SaveStore.Save(state);
                app.Toast($"{Korean.Ga(owned.Name)} 되었습니다.");
                app.Rebuild();
            }, can ? Theme.Accent : Theme.PanelHigh);
            button.interactable = can;
        }

        // ------------------------------------------------------------ 스킬

        private static void BuildSkills(Transform column, App app, Owned owned)
        {
            var state = app.State;
            var row = owned.Row;

            var usable = WildlingData.MonsterSkill.Records
                .Where(r => r.MonsterId == owned.MonsterId && r.UnlockStage <= row.Stage)
                .ToList();

            foreach (string skillId in owned.Active)
            {
                var skill = WildlingData.Skill.FindBySkillId(skillId);
                CodexEntryScreen.SkillRow(column, skill, SlotKind.Active, row.Stage);
            }

            foreach (string skillId in owned.Passive)
            {
                var skill = WildlingData.Skill.FindBySkillId(skillId);
                CodexEntryScreen.SkillRow(column, skill, SlotKind.Passive, row.Stage);
            }

            var spare = usable
                .Where(r => !owned.Active.Contains(r.SkillId) && !owned.Passive.Contains(r.SkillId))
                .ToList();

            if (spare.Count == 0)
                return;

            App.Section(column, "슬롯에 넣지 않은 것");
            foreach (var candidate in spare)
            {
                var item = Ui.Item(column, 70f);
                var skill = candidate.SkillBySkillId;
                Ui.Button(item.transform,
                          $"{skill?.Name ?? candidate.SkillId} 를 슬롯에 넣기", () =>
                          {
                              var slots = candidate.SlotKind == SlotKind.Active
                                  ? owned.Active
                                  : owned.Passive;
                              if (slots.Count > 0)
                                  slots.RemoveAt(slots.Count - 1);
                              slots.Add(candidate.SkillId);
                              state.FitSkills(owned);
                              SaveStore.Save(state);
                              app.Rebuild();
                          });
            }
        }
    }
}
