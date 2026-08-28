using System;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using Wildling.Data;

namespace Wildling.Game
{
    /// <summary>
    /// 첫 화면이다. 탐사의 상태와 파티를 보인다.
    /// </summary>
    /// <remarks>
    /// 배경은 `Region.background` 가 가리키는 그림입니다 — `asset=model` 컬럼이 실제로
    /// 화면에 닿는 자리입니다.
    /// </remarks>
    public sealed class HomeScreen : Screen
    {
        public override string Title => "탐사관";

        public override void Build(Transform root, App app)
        {
            var state = app.State;
            string regionId = string.IsNullOrEmpty(state.ExpeditionRegionId)
                ? state.UnlockedRegions.FirstOrDefault() ?? "weir_forest"
                : state.ExpeditionRegionId;
            var region = WildlingData.Region.FindByRegionId(regionId);

            // 배경
            if (region != null)
            {
                var bg = Ui.Node("bg", root);
                var rt = Ui.Rect(bg);
                rt.anchorMin = new Vector2(0f, 1f);
                rt.anchorMax = new Vector2(1f, 1f);
                rt.pivot = new Vector2(0.5f, 1f);
                rt.sizeDelta = new Vector2(0f, 360f);
                Parallax.Attach(bg, ArtLibrary.Model(region.Background), 18f);

                var fade = Ui.Node("fade", bg.transform);
                Ui.Stretch(fade);
                var tint = fade.AddComponent<Image>();
                tint.color = new Color(Theme.Background.r, Theme.Background.g,
                                       Theme.Background.b, 0.35f);
                tint.raycastTarget = false;

                var name = Ui.Node("name", bg.transform);
                var nrt = Ui.Rect(name);
                nrt.anchorMin = new Vector2(0f, 0f);
                nrt.anchorMax = new Vector2(1f, 0f);
                nrt.pivot = new Vector2(0.5f, 0f);
                nrt.offsetMin = new Vector2(16f, 26f);
                nrt.offsetMax = new Vector2(-16f, 76f);
                Ui.Label(name.transform, region.Name, 30, Theme.OnDark);
            }

            var body = Ui.Node("body", root);
            var brt = Ui.Rect(body);
            brt.anchorMin = Vector2.zero;
            brt.anchorMax = Vector2.one;
            brt.offsetMin = new Vector2(0f, 0f);
            brt.offsetMax = new Vector2(0f, -368f);
            var column = Ui.Scroll(body.transform);

            NextGoal.Build(column, app);
            BuildExpedition(column, app, region);
            BuildParty(column, app);
            BuildProgress(column, app);
        }

        // ------------------------------------------------------------ 탐사

        private static void BuildExpedition(Transform column, App app, RegionRecord region)
        {
            var state = app.State;
            App.Section(column, "탐사");

            bool running = !string.IsNullOrEmpty(state.ExpeditionRegionId);
            var inner = Ui.Card(column, Theme.Panel, 14f, 8f);
            var card = inner.gameObject;

            if (running)
            {
                int cap = IdleConst.CapHours * 3600;

                var head = Ui.Item(inner, 34f);
                var headLabel = Ui.Label(head.transform, "", 24);

                var bar = Ui.Item(inner, 16f);
                var fill = Ui.Bar(bar.transform, 0f, Theme.Accent);

                var note = Ui.Item(inner, 30f);
                var noteLabel = Ui.Label(note.transform, "", 20, Theme.TextDim);

                // 정산은 「다음 목표」에 이미 있습니다. 같은 버튼을 두 번 두지 않습니다.
                var buttons = Ui.Item(inner, 62f);
                Ui.Button(buttons.transform, "탐사지 변경",
                          () => app.Go(new ExpeditionScreen()));

                // **손대지 않아도 흐릅니다.** 방치형이므로 화면이 멈춰 있으면 진행이 멈춘
                // 것처럼 보입니다. 화면을 다시 조립하지 않고 이 셋만 갱신합니다.
                var ticker = card.AddComponent<Ticker>();
                ticker.Interval = 1f;
                ticker.Tick = () =>
                {
                    if (headLabel == null || fill == null || noteLabel == null)
                        return;

                    int elapsed = (int)Math.Max(0, Clock.NowUtc - state.ExpeditionStartedUtc);
                    int capped = Math.Min(elapsed, cap);
                    bool full = capped >= cap;

                    headLabel.text = $"{region?.Name ?? state.ExpeditionRegionId} 에서 "
                                     + $"{Expedition.Elapsed(capped)} 누적";

                    var frt = (RectTransform)fill.transform;
                    frt.anchorMax = new Vector2(capped / (float)cap, 1f);
                    fill.color = full ? Theme.Warn : Theme.Accent;

                    noteLabel.text = full
                        ? $"상한 {IdleConst.CapHours}시간에 도달해 누적이 멈추었습니다."
                        : $"상한은 {IdleConst.CapHours}시간입니다. 접속하지 않아도 쌓입니다.";
                    noteLabel.color = full ? Theme.Warn : Theme.TextDim;
                };
                ticker.Tick();
            }
            else
            {
                var head = Ui.Item(inner, 34f);
                Ui.Label(head.transform, "파견 중인 탐사가 없습니다.", 24, Theme.TextDim);

                var buttons = Ui.Item(inner, 62f);
                Ui.Button(buttons.transform, "탐사 보내기",
                          () => app.Go(new ExpeditionScreen()), Theme.Accent);
            }
        }

        /// <summary>
        /// 탐사를 정산한다.
        /// </summary>
        /// <remarks>
        /// **여기가 표 여럿이 한 번에 닿는 자리입니다** — `RegionYield` 8구간, 그 구간마다의
        /// `RewardGroup`, `EncounterTable` 의 가중치 추첨, 그리고 `Monster` 의 기록 상태.
        /// </remarks>
        public static void Settle(App app)
        {
            var state = app.State;
            long now = Clock.NowUtc;

            var rng = new Rng((int)(state.ExpeditionStartedUtc ^ now));
            var result = Expedition.Settle(state, state.ExpeditionRegionId,
                                           state.ExpeditionStartedUtc, now, rng);

            var report = state.Apply(result.Grants);

            // 정산하면 그 자리에서 다시 파견됩니다. 방치형의 루프가 끊기지 않게 하는 것입니다.
            state.ExpeditionStartedUtc = now;
            SaveStore.Save(state);

            app.Go(new SettleScreen(result, report));
        }

        // ------------------------------------------------------------ 파티

        private static void BuildParty(Transform column, App app)
        {
            App.Section(column, $"파티 {app.State.ActiveParty + 1}");

            var members = app.State.PartyMembers();
            if (members.Count == 0)
            {
                var empty = Ui.Item(column, 70f);
                Ui.Button(empty.transform, "파티를 편성하십시오",
                          () => app.Go(new PartyScreen()));
                return;
            }

            foreach (var owned in members)
            {
                var row = owned.Row;
                if (row is null)
                    continue;
                var stats = Stats.Compute(row, owned.Level, app.State.Resonance(owned.SpeciesId));
                App.MonsterRow(column, row,
                               $"Lv {owned.Level}\n{stats.Hp} · {stats.Attack}",
                               () => app.Go(new MonsterScreen(owned.Uid)));
            }
        }

        // ------------------------------------------------------------ 진척

        private static void BuildProgress(Transform column, App app)
        {
            App.Section(column, "기록부");

            int completion = app.State.Completion();
            var inner = Ui.Card(column, Theme.Panel, 14f, 8f);

            var head = Ui.Item(inner, 30f);
            Ui.Label(head.transform,
                     $"전체 완성률 {Numbers.AsPercent(completion)} — "
                     + $"{WildlingData.Monster.Records.Count}행 중 "
                     + $"{WildlingData.Monster.Records.Count(r => app.State.CodexState(r.MonsterId) >= CodexState.Recorded)}행",
                     22);

            var bar = Ui.Item(inner, 16f);
            Ui.Bar(bar.transform, completion / (float)Numbers.One, Theme.Accent);
        }
    }

    /// <summary>탐사를 보낼 지역을 고른다.</summary>
    public sealed class ExpeditionScreen : Screen
    {
        public override string Title => "탐사";

        public override void Build(Transform root, App app)
        {
            var column = Ui.Scroll(root);
            App.Section(column, "지역을 고르십시오");

            foreach (var region in WildlingData.Region.Records.OrderBy(r => r.Order))
            {
                bool open = app.State.IsUnlocked(region.RegionId);
                var item = Ui.Item(column, 150f);

                if (open)
                {
                    Ui.Button(item.transform, "", () =>
                    {
                        app.State.ExpeditionRegionId = region.RegionId;
                        app.State.ExpeditionStartedUtc = Clock.NowUtc;
                        SaveStore.Save(app.State);
                        app.Toast($"{region.Name} 으로 파견했습니다.");
                        app.Go(new HomeScreen(), false);
                    }, Theme.Panel);
                }
                else
                {
                    Ui.Panel(item.transform, Theme.Panel);
                }

                var bg = Ui.Node("bg", item.transform);
                var brt = Ui.Rect(bg);
                brt.anchorMin = new Vector2(0f, 0f);
                brt.anchorMax = new Vector2(0f, 1f);
                brt.pivot = new Vector2(0f, 0.5f);
                brt.sizeDelta = new Vector2(220f, 0f);
                var image = Ui.Icon(bg.transform, ArtLibrary.Model(region.Background));
                image.preserveAspect = false;
                image.color = open ? Color.white : new Color(0.35f, 0.35f, 0.4f, 1f);

                var text = Ui.Node("text", item.transform);
                var trt = Ui.Stretch(text);
                trt.offsetMin = new Vector2(234f, 10f);
                trt.offsetMax = new Vector2(-12f, -10f);
                var lines = Ui.Column(text.transform, 4f);

                var name = Ui.Item(lines, 34f);
                Ui.Label(name.transform, open ? region.Name : "???", 26,
                         open ? Theme.Text : Theme.TextDim);

                var element = Ui.Item(lines, 28f);
                Ui.Label(element.transform,
                         $"{Theme.Label(region.ThemeElement)} 우세 · 완성률 "
                         + Numbers.AsPercent(app.State.Completion(region.RegionId)),
                         20, Theme.TextDim);

                var yield = WildlingData.RegionYield
                    .FindByRegionIdAndHourBand(region.RegionId, 0);
                if (yield != null && open)
                {
                    var line = Ui.Item(lines, 28f);
                    Ui.Label(line.transform,
                             $"첫 시간 은편 {yield.GoldPerHour} · 먹이 {yield.FoodPerHour}",
                             20, Theme.Accent);
                }

                if (!open && region.HasRequirementGroupId)
                {
                    var checks = Requirements.Evaluate(region.RequirementGroupId, app.State, null);
                    var line = Ui.Item(lines, 28f);
                    Ui.Label(line.transform,
                             "해금 조건 — " + string.Join(" · ", checks.Select(c => c.Text)),
                             20, Theme.Warn);
                }
            }
        }
    }

    /// <summary>정산 결과이다.</summary>
    public sealed class SettleScreen : Screen
    {
        private readonly ExpeditionResult _result;
        private readonly GrantReport _report;

        public SettleScreen(ExpeditionResult result, GrantReport report)
        {
            _result = result;
            _report = report;
        }

        public override string Title => "탐사 정산";

        public override void Build(Transform root, App app)
        {
            var column = Ui.Scroll(root);

            var head = Ui.Item(column, 60f);
            Ui.Label(head.transform,
                     $"{Expedition.Elapsed(_result.CappedSeconds)} 분의 산출입니다."
                     + (_result.HitCap ? " 상한에 도달했습니다." : ""),
                     24, _result.HitCap ? Theme.Warn : Theme.Text);

            if (_result.Discovered.Count > 0)
            {
                App.Section(column, "새로 기록한 종");
                foreach (string monsterId in _result.Discovered)
                {
                    var row = WildlingData.Monster.FindByMonsterId(monsterId);
                    if (row != null)
                        App.MonsterRow(column, row, "신규",
                                       () => app.Go(new CodexScreen()));
                }
            }

            // 받은 것이 있으면 그 자리에서 터집니다.
            if (_result.Grants.Count > 0)
            {
                Fx.Burst(app.Effects, new Vector2(0f, 40f), Theme.Accent, 460f);
                Fx.Sparks(app.Effects, new Vector2(0f, 40f), Theme.Accent, 12, 240f);
            }
            if (_result.Discovered.Count > 0)
            {
                Fx.Flash(app.Effects, Color.white, 0.36f);
                Fx.Shout(app.Effects, "새 기록!", Theme.Accent, 72);
            }

            App.Section(column, "받은 것");
            if (_result.Grants.Count == 0)
            {
                var empty = Ui.Item(column, 50f);
                Ui.Label(empty.transform, "아직 쌓인 것이 없습니다.", 22, Theme.TextDim);
            }
            foreach (var grant in _result.Grants)
                App.GrantRow(column, grant);

            if (_report.Lines.Count > 0)
            {
                App.Section(column, "일어난 일");
                foreach (string line in _report.Lines)
                {
                    var item = Ui.Item(column, 40f);
                    Ui.Label(item.transform, line, 20, Theme.TextDim);
                }
            }

            var buttons = Ui.Item(column, 70f);
            Ui.Button(buttons.transform, "돌아가기",
                      () => app.Go(new HomeScreen(), false), Theme.Accent);
        }
    }
}
