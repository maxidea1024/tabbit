using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using Wildling.Data;

namespace Wildling.Game
{
    /// <summary>
    /// 지금 할 수 있는 것 하나를 고른다.
    /// </summary>
    /// <remarks>
    /// **기획서 2.2 가 정한 것입니다** — 「한 바퀴가 끝날 때 다음 바퀴의 목표가 보여야
    /// 합니다」. 그 표의 오른쪽 칸이 비는 상태를 기획서는 **정체**라고 부르고, 이 카드가
    /// 비는 것이 곧 그 상태입니다.
    ///
    /// 순서는 기획서 2.1 의 루프 순서입니다 — 정산이 먼저이고 도전이 마지막입니다.
    /// </remarks>
    public static class NextGoal
    {
        public struct Goal
        {
            public string Head;
            public string Detail;
            public string Button;
            public System.Func<Screen> Go;
            public bool Urgent;
        }

        public static Goal Pick(GameState state)
        {
            // 1. 쌓인 탐사가 있으면 정산이 먼저입니다.
            if (!string.IsNullOrEmpty(state.ExpeditionRegionId))
            {
                int elapsed = (int)System.Math.Max(0, Clock.NowUtc - state.ExpeditionStartedUtc);
                int cap = IdleConst.CapHours * 3600;
                if (elapsed >= 600)
                {
                    return new Goal
                    {
                        Head = "탐사를 정산하십시오",
                        Detail = $"{Expedition.Elapsed(System.Math.Min(elapsed, cap))} 쌓였습니다."
                                 + (elapsed >= cap ? " 상한에 도달해 더 쌓이지 않습니다." : ""),
                        Button = "정산",
                        Go = null,
                        Urgent = elapsed >= cap,
                    };
                }
            }
            else
            {
                return new Goal
                {
                    Head = "탐사를 보내십시오",
                    Detail = "파견해 두면 접속하지 않아도 쌓입니다.",
                    Button = "탐사",
                    Go = () => new ExpeditionScreen(),
                };
            }

            // 2. 각성할 수 있는 개체.
            var ready = state.All.FirstOrDefault(o => state.CanAwaken(o, out _));
            if (ready != null)
            {
                var link = state.AwakeningOf(ready);
                return new Goal
                {
                    Head = $"{ready.Name} 이(가) 각성할 수 있습니다",
                    Detail = $"{link?.MonsterByToMonsterId?.Name ?? "다음 단계"} 로 올라가고 "
                             + "레벨 상한과 스킬 슬롯이 늘어납니다.",
                    Button = "각성",
                    Go = () => new MonsterScreen(ready.Uid),
                    Urgent = true,
                };
            }

            // 3. 받을 수 있는 기록부 완성 보상.
            var codex = state.PendingCodexRewards().FirstOrDefault();
            if (codex != null)
            {
                return new Goal
                {
                    Head = "기록부 완성 보상이 있습니다",
                    Detail = $"{(codex.HasRegionId ? codex.RegionByRegionId?.Name ?? codex.RegionId : "전체")}"
                             + $" {codex.Threshold}% 구간입니다.",
                    Button = "기록부",
                    Go = () => new CodexScreen(),
                };
            }

            // 4. 올릴 수 있는 레벨.
            var growable = state.PartyMembers().FirstOrDefault(state.CanLevelUp);
            if (growable != null)
            {
                return new Goal
                {
                    Head = $"{growable.Name} 을(를) 키울 수 있습니다",
                    Detail = $"지금 {growable.Level}레벨이고 이 단계의 상한은 "
                             + $"{state.LevelCap(growable)} 입니다.",
                    Button = "육성",
                    Go = () => new MonsterScreen(growable.Uid),
                };
            }

            // 5. 도전할 수 있는 스테이지.
            var stage = WildlingData.Stage.Records
                .Where(s => state.IsStageOpen(s) && !state.IsCleared(s.StageId))
                .OrderBy(s => s.RegionByRegionId?.Order ?? 99)
                .ThenBy(s => s.Index)
                .FirstOrDefault();
            if (stage != null)
            {
                var wave = stage.MonsterByWaveMonsterIds ?? System.Array.Empty<MonsterTable.Record>();
                return new Goal
                {
                    Head = $"{stage.RegionByRegionId?.Name ?? stage.RegionId} "
                           + $"{stage.Index}번에 도전하십시오",
                    Detail = $"{Theme.Label(stage.StageKind)} · "
                             + string.Join(" · ", wave.Where(m => m != null)
                                 .Select(m => $"{m.Name}({Theme.Label(m.Element)})")),
                    Button = "도전",
                    Go = () => new BattleScreen(stage.StageId),
                    Urgent = stage.StageKind == StageKind.Guardian,
                };
            }

            // 6. 여기가 비면 기획서가 말하는 **정체**입니다.
            return new Goal
            {
                Head = "다음 목표가 없습니다",
                Detail = "탐사로 미기록 종을 찾거나 조각을 모으십시오.",
                Button = "탐사",
                Go = () => new ExpeditionScreen(),
            };
        }

        /// <summary>고른 것을 카드 하나로 그린다.</summary>
        public static void Build(Transform column, App app)
        {
            var goal = Pick(app.State);

            var card = Ui.Item(column, 176f);
            Ui.Panel(card.transform, Theme.PanelHigh);

            var stripe = Ui.Node("stripe", card.transform);
            var srt = Ui.Rect(stripe);
            srt.anchorMin = new Vector2(0f, 0f);
            srt.anchorMax = new Vector2(0f, 1f);
            srt.pivot = new Vector2(0f, 0.5f);
            srt.sizeDelta = new Vector2(6f, 0f);
            var strip = stripe.AddComponent<Image>();
            strip.color = goal.Urgent ? Theme.Accent : Theme.Line;
            strip.raycastTarget = false;

            var inner = Ui.Column(card.transform, 6f, 14f);

            var tag = Ui.Item(inner, 26f);
            Ui.Label(tag.transform, "다음 목표", 19, Theme.TextDim);

            var head = Ui.Item(inner, 34f);
            Ui.Label(head.transform, goal.Head, 25, goal.Urgent ? Theme.Accent : Theme.Text);

            var detail = Ui.Item(inner, 30f);
            Ui.Label(detail.transform, goal.Detail, 19, Theme.TextDim);

            var buttons = Ui.Item(inner, 56f);
            var button = Ui.Button(buttons.transform, goal.Button, () =>
            {
                if (goal.Go is null)
                    HomeScreen.Settle(app);
                else
                    app.Go(goal.Go());
            }, Theme.Accent);

            // **지금 눌러야 하는 것 하나에만 빛이 지나갑니다.** 전부에 붙이면 아무것도
            // 눈에 띄지 않습니다.
            Shine.Attach(button.gameObject, goal.Urgent ? 1.6f : 2.8f);
        }
    }
}
