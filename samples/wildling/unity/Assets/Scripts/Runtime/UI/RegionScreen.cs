using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using Wildling.Data;

namespace Wildling.Game
{
    /// <summary>
    /// 지역과 스테이지 목록이다.
    /// </summary>
    /// <remarks>
    /// `Stage` 의 키가 둘입니다 — `stage_id` 하나와 `(region_id, index)` 조합. 참조 대상이
    /// 되려면 단일 키가 필요하고, 화면은 조합 쪽으로 찾습니다.
    /// </remarks>
    public sealed class RegionScreen : Screen
    {
        private readonly string _regionId;

        public RegionScreen(string regionId = null) => _regionId = regionId;

        public override string Title => "지역";

        public override void Build(Transform root, App app)
        {
            var state = app.State;
            state.UnlockReady();

            string regionId = _regionId
                              ?? state.UnlockedRegions
                                  .OrderByDescending(GameState.RegionOrder)
                                  .FirstOrDefault()
                              ?? "weir_forest";

            var column = Ui.Scroll(root);

            // 지역 고르기
            var tabs = Ui.Item(column, 62f);
            var tabRow = Ui.Row(tabs.transform, 6f);
            foreach (var region in WildlingData.Region.Records.OrderBy(r => r.Order))
            {
                bool open = state.IsUnlocked(region.RegionId);
                string id = region.RegionId;
                // **잠긴 지역도 이름은 보입니다.** 기획서 11.2 가 가리는 것은 그 지역의
                // 종이지 지역 자체가 아닙니다.
                var button = Ui.Button(tabRow, region.Name,
                                       () => app.Go(new RegionScreen(id), false),
                                       id == regionId ? Theme.Accent : Theme.PanelHigh, 20);
                var label = button.GetComponentInChildren<Text>();
                if (label != null)
                    label.color = open ? Theme.Text : Theme.TextDim;
            }

            var current = WildlingData.Region.FindByRegionId(regionId);
            if (current is null)
                return;

            if (!state.IsUnlocked(regionId))
            {
                App.Section(column, "해금 조건");
                foreach (var check in Requirements.Evaluate(
                             current.RequirementGroupId, state, null))
                {
                    var line = Ui.Item(column, 40f);
                    Ui.Label(line.transform, (check.Met ? "충족  " : "부족  ") + check.Text,
                             22, check.Met ? Theme.Good : Theme.Warn);
                }
                return;
            }

            int highest = state.HighestCleared(regionId);
            var stages = WildlingData.Stage.Records
                .Where(s => s.RegionId == regionId)
                .OrderBy(s => s.Index)
                .ToList();

            App.Section(column,
                        $"{current.Name} — {highest} / {stages.Count} 클리어");

            foreach (var stage in stages)
                StageRow(column, app, stage);
        }

        private static void StageRow(Transform column, App app, StageTable.Record stage)
        {
            var state = app.State;
            bool open = state.IsStageOpen(stage);
            bool cleared = state.IsCleared(stage.StageId);

            var item = Ui.Item(column, 108f);
            if (open)
                Ui.Button(item.transform, "", () => app.Go(new BattleScreen(stage.StageId)),
                          Theme.Panel);
            else
                Ui.Panel(item.transform, Theme.Panel);

            var text = Ui.Node("text", item.transform);
            var trt = Ui.Stretch(text);
            trt.offsetMin = new Vector2(14f, 8f);
            trt.offsetMax = new Vector2(-160f, -8f);
            var lines = Ui.Column(text.transform, 3f);

            var head = Ui.Item(lines, 32f);
            Ui.Label(head.transform,
                     $"{stage.Index}. {Theme.Label(stage.StageKind)}"
                     + (cleared ? "   클리어" : ""),
                     24, open ? Theme.Text : Theme.TextDim);

            // 등장 목록은 셀 안의 참조 배열입니다 — 키가 아니라 행으로 옵니다.
            var wave = stage.MonsterByWaveMonsterIds ?? System.Array.Empty<MonsterTable.Record>();
            var names = new List<string>();
            for (int i = 0; i < wave.Length; i++)
            {
                int level = i < stage.WaveLevels.Length ? stage.WaveLevels[i] : 1;
                names.Add($"{wave[i]?.Name ?? "?"} Lv{level}");
            }

            var enemies = Ui.Item(lines, 28f);
            Ui.Label(enemies.transform, string.Join(" · ", names), 19, Theme.TextDim);

            var reward = Ui.Item(lines, 26f);
            var grants = Rewards.Entries(stage.RewardGroupId)
                .Select(e => Rewards.Describe(Rewards.ToGrant(e.Reward)));
            Ui.Label(reward.transform, string.Join(" · ", grants), 18, Theme.Accent);

            // 등장 아이콘
            var icons = Ui.Node("icons", item.transform);
            var irt = Ui.Rect(icons);
            irt.anchorMin = new Vector2(1f, 0.5f);
            irt.anchorMax = new Vector2(1f, 0.5f);
            irt.pivot = new Vector2(1f, 0.5f);
            irt.sizeDelta = new Vector2(150f, 72f);
            irt.anchoredPosition = new Vector2(-10f, 0f);
            var iconRow = Ui.Row(icons.transform, 4f);
            foreach (var monster in wave.Take(3))
            {
                var cell = Ui.Node("i", iconRow);
                var image = Ui.Icon(cell.transform, ArtLibrary.Icon(monster?.Icon));
                if (!open)
                    image.color = new Color(0.4f, 0.4f, 0.46f, 1f);
            }
        }
    }
}
