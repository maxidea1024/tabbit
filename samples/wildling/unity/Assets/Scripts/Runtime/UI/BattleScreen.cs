using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using Wildling.Data;

namespace Wildling.Game
{
    /// <summary>
    /// 전투 관전이다.
    /// </summary>
    /// <remarks>
    /// **계산은 시작할 때 한 번에 끝납니다.** 화면은 그 기록을 순서대로 재생하기만 하므로
    /// 배속이 결과를 바꾸지 않습니다 — 기획서 9.2 가 「전투 중 입력은 없다」고 정한 것의
    /// 구현입니다.
    /// </remarks>
    public sealed class BattleScreen : Screen
    {
        private readonly string _stageId;
        private BattleReport _report;
        private Transform _logRoot;
        private RectTransform _logColumn;
        private App _app;
        private int _shown;
        private static int _speedIndex;
        private readonly List<Image> _partyBars = new();
        private readonly List<Image> _enemyBars = new();

        public BattleScreen(string stageId) => _stageId = stageId;

        public override string Title => "전투";

        public override void Build(Transform root, App app)
        {
            _app = app;
            var state = app.State;
            var stage = WildlingData.Stage.FindByStageId(_stageId);
            if (stage is null)
            {
                Ui.Label(root, "그 스테이지가 없습니다.", 24, Theme.TextDim,
                         TextAnchor.MiddleCenter);
                return;
            }

            var party = BuildParty(state);
            if (party.Count == 0)
            {
                Ui.Label(root, "파티가 비어 있습니다.", 24, Theme.Warn, TextAnchor.MiddleCenter);
                return;
            }

            var enemies = BuildEnemies(stage);

            _report = Battle.Run(party, enemies,
                                 (int)(Clock.NowUtc ^ _stageId.GetHashCode()));

            // 만난 것은 목격 상태가 됩니다.
            foreach (var enemy in enemies)
                state.SetCodex(enemy.Monster.MonsterId, CodexState.Sighted);

            BuildLayout(root, stage);
            app.StartCoroutine(Play(stage));
        }

        // ------------------------------------------------------------ 편성

        private static List<Combatant> BuildParty(GameState state)
        {
            var list = new List<Combatant>();
            var members = state.PartyMembers();

            for (int i = 0; i < members.Count; i++)
            {
                var owned = members[i];
                if (owned.Row is null)
                    continue;

                list.Add(new Combatant
                {
                    Monster = owned.Row,
                    Level = owned.Level,
                    Resonance = state.Resonance(owned.SpeciesId),
                    Placement = i,
                    IsEnemy = false,
                    Active = owned.Active
                        .Select(id => WildlingData.Skill.FindBySkillId(id))
                        .Where(s => s != null).ToArray(),
                    Passive = owned.Passive
                        .Select(id => WildlingData.Skill.FindBySkillId(id))
                        .Where(s => s != null).ToArray(),
                    SkillLevels = owned.SkillLevels.ToArray(),
                });
            }
            return list;
        }

        /// <summary>
        /// 등장 목록을 전투에 세운다.
        /// </summary>
        /// <remarks>
        /// **수호자는 `Boss` 가 능력치 배수를 얹습니다.** 같은 종이라도 수호자로 나오면 다른
        /// 상대가 되는 자리이고, 그 배수가 표에 있습니다.
        /// </remarks>
        private static List<Combatant> BuildEnemies(StageTable.Record stage)
        {
            var list = new List<Combatant>();
            var wave = stage.MonsterByWaveMonsterIds ?? System.Array.Empty<MonsterTable.Record>();

            for (int i = 0; i < wave.Length; i++)
            {
                var monster = wave[i];
                if (monster is null)
                    continue;

                int level = i < stage.WaveLevels.Length ? stage.WaveLevels[i] : 1;
                var boss = stage.StageKind == StageKind.Guardian
                    ? WildlingData.Boss.Records
                        .FirstOrDefault(b => b.MonsterId == monster.MonsterId)
                    : null;

                var skills = WildlingData.MonsterSkill.Records
                    .Where(r => r.MonsterId == monster.MonsterId
                                && r.UnlockStage <= monster.Stage)
                    .ToList();

                list.Add(new Combatant
                {
                    Monster = monster,
                    Level = level,
                    Placement = i,
                    IsEnemy = true,
                    BossStatFactor = boss?.StatFactor.Attack ?? Numbers.One,
                    Active = skills.Where(r => r.SlotKind == SlotKind.Active)
                        .Take(Stats.ActiveSlots(monster.Stage))
                        .Select(r => r.SkillBySkillId).Where(s => s != null).ToArray(),
                    Passive = skills.Where(r => r.SlotKind == SlotKind.Passive)
                        .Take(Stats.PassiveSlots(monster.Stage))
                        .Select(r => r.SkillBySkillId).Where(s => s != null).ToArray(),
                });
            }
            return list;
        }

        // ------------------------------------------------------------ 화면

        private void BuildLayout(Transform root, StageTable.Record stage)
        {
            var region = stage.RegionByRegionId;
            if (region != null)
            {
                var bg = Ui.Node("bg", root);
                Ui.Stretch(bg);
                var image = Ui.Icon(bg.transform, ArtLibrary.Model(region.Background));
                image.preserveAspect = false;
                image.color = new Color(0.55f, 0.55f, 0.62f, 1f);
            }

            var head = Ui.Node("head", root);
            Ui.Top(head, 64f);
            Ui.Label(head.transform,
                     $"{region?.Name ?? stage.RegionId} {stage.Index} — "
                     + Theme.Label(stage.StageKind),
                     24, Theme.Text, TextAnchor.MiddleCenter);

            var enemies = Ui.Node("enemies", root);
            Ui.Top(enemies, 200f, 68f);
            Side(enemies.transform, _report.Enemies, _enemyBars);

            var party = Ui.Node("party", root);
            Ui.Top(party, 200f, 276f);
            Side(party.transform, _report.Party, _partyBars);

            var logBox = Ui.Node("log", root);
            var rt = Ui.Rect(logBox);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(10f, 76f);
            rt.offsetMax = new Vector2(-10f, -486f);
            var panel = logBox.AddComponent<Image>();
            panel.color = new Color(0f, 0f, 0f, 0.55f);
            _logRoot = logBox.transform;
            _logColumn = Ui.Scroll(logBox.transform, 2f, 8f);

            var buttons = Ui.Node("buttons", root);
            Ui.Bottom(buttons, 64f, 6f);
            var row = Ui.Row(buttons.transform, 8f);

            foreach (var speed in WildlingData.BattleSpeed.Records.OrderBy(s => s.At))
            {
                int at = speed.At;
                Ui.Button(row, speed.Label, () =>
                {
                    _speedIndex = at;
                    _app.Toast($"{speed.Label} 로 봅니다.");
                }, _speedIndex == at ? Theme.Accent : Theme.PanelHigh, 20);
            }

            Ui.Button(row, "건너뛰기", () =>
            {
                while (_shown < _report.Beats.Count)
                    AppendLine();
            }, Theme.PanelHigh, 20);
        }

        private static void Side(Transform parent, List<Combatant> side, List<Image> bars)
        {
            var row = Ui.Row(parent, 8f, 8f);
            foreach (var c in side)
            {
                var cell = Ui.Node(c.Monster.MonsterId, row);
                Ui.Panel(cell.transform, new Color(0f, 0f, 0f, 0.35f));

                var icon = Ui.Node("icon", cell.transform);
                var irt = Ui.Rect(icon);
                irt.anchorMin = new Vector2(0.5f, 1f);
                irt.anchorMax = new Vector2(0.5f, 1f);
                irt.pivot = new Vector2(0.5f, 1f);
                irt.sizeDelta = new Vector2(104f, 104f);
                irt.anchoredPosition = new Vector2(0f, -6f);
                Ui.Icon(icon.transform, ArtLibrary.Icon(c.Monster.Icon));

                var name = Ui.Node("name", cell.transform);
                var nrt = Ui.Rect(name);
                nrt.anchorMin = new Vector2(0f, 0f);
                nrt.anchorMax = new Vector2(1f, 0f);
                nrt.pivot = new Vector2(0.5f, 0f);
                nrt.offsetMin = new Vector2(4f, 30f);
                nrt.offsetMax = new Vector2(-4f, 54f);
                Ui.Label(name.transform, $"{c.Name} Lv{c.Level}", 17, Theme.Text,
                         TextAnchor.MiddleCenter);

                var bar = Ui.Node("hp", cell.transform);
                var brt = Ui.Rect(bar);
                brt.anchorMin = new Vector2(0f, 0f);
                brt.anchorMax = new Vector2(1f, 0f);
                brt.pivot = new Vector2(0.5f, 0f);
                brt.offsetMin = new Vector2(6f, 8f);
                brt.offsetMax = new Vector2(-6f, 22f);
                // 재생이 그 시점의 값으로 채웁니다. 처음에는 가득입니다.
                bars.Add(Ui.Bar(bar.transform, 1f, Theme.Good));
            }
        }

        // ------------------------------------------------------------ 재생

        private IEnumerator Play(StageTable.Record stage)
        {
            var speedRow = WildlingData.BattleSpeed.Records
                .FirstOrDefault(s => s.At == _speedIndex);
            int multiplier = System.Math.Max(1, speedRow?.Multiplier ?? 1);

            while (_shown < _report.Beats.Count)
            {
                AppendLine();
                yield return new WaitForSeconds(0.24f / multiplier);
            }

            yield return new WaitForSeconds(0.4f);
            Finish(stage);
        }

        private void AppendLine()
        {
            if (_shown >= _report.Beats.Count || _logColumn == null)
                return;

            var beat = _report.Beats[_shown++];
            bool detail = beat.Text.StartsWith("  ");

            var item = Ui.Item(_logColumn, detail ? 26f : 30f);
            Ui.Label(item.transform, beat.Text, detail ? 19 : 22,
                     detail ? Theme.TextDim : Theme.Text);

            Fill(_partyBars, _report.Party, beat.PartyHp);
            Fill(_enemyBars, _report.Enemies, beat.EnemyHp);

            var scroll = _logRoot.GetComponent<ScrollRect>();
            if (scroll != null)
                scroll.verticalNormalizedPosition = 0f;
        }

        /// <summary>그 박자의 체력으로 막대를 채운다.</summary>
        private static void Fill(List<Image> bars, List<Combatant> side, int[] hp)
        {
            if (hp is null)
                return;

            for (int i = 0; i < bars.Count && i < side.Count && i < hp.Length; i++)
            {
                if (bars[i] is null)
                    continue;
                float ratio = side[i].MaxHp <= 0 ? 0f : Mathf.Clamp01(hp[i] / (float)side[i].MaxHp);
                var rt = (RectTransform)bars[i].transform;
                rt.anchorMax = new Vector2(ratio, 1f);
                bars[i].color = hp[i] > 0 ? Theme.Good : Theme.Warn;
            }
        }

        /// <summary>
        /// 결과를 상태에 반영한다.
        /// </summary>
        /// <remarks>
        /// **여기가 승리 하나로 표 여섯이 움직이는 자리입니다** — `StageReward` 의 첫 클리어와
        /// 반복 보상, `RewardEntry` 의 변종 지급, `CodexConst` 의 전투 관측, 관측 스테이지의
        /// 목격 전이, 그리고 `RequirementGroup` 을 다시 확인해 열리는 지역.
        /// </remarks>
        private void Finish(StageTable.Record stage)
        {
            var state = _app.State;
            var grants = new List<Grant>();
            var opened = new List<RegionTable.Record>();
            bool firstClear = false;

            if (_report.PartyWon)
            {
                firstClear = state.ClearStage(stage);

                var rng = new Rng((int)Clock.NowUtc ^ stage.StageId.GetHashCode());
                grants.AddRange(Rewards.Roll(stage.RewardGroupId, rng));

                var extra = WildlingData.StageReward.Records
                    .FirstOrDefault(r => r.StageId == stage.StageId);
                if (extra != null)
                {
                    grants.AddRange(Rewards.Roll(extra.RewardGroupId, rng));
                    if (firstClear && extra.HasFirstClearGroupId)
                        grants.AddRange(Rewards.Certain(extra.FirstClearGroupId));
                }

                // 전투 승리도 관측을 누적합니다. 계수는 `CodexConst` 입니다.
                foreach (var enemy in _report.Enemies)
                    state.ObserveFromBattle(enemy.Monster.MonsterId, 1);

                // 관측 스테이지는 미기록 종 하나를 목격 상태로 만듭니다.
                if (stage.StageKind == StageKind.Observation)
                    SightOneUnknown(state, stage.RegionId);

                opened = state.UnlockReady();
            }

            var report = state.Apply(Rewards.Merge(grants));
            SaveStore.Save(state);

            _app.Go(new BattleResultScreen(_report, stage, report, firstClear, opened), false);
        }

        private static void SightOneUnknown(GameState state, string regionId)
        {
            long mask = 1L << (GameState.RegionOrder(regionId) - 1);
            var target = WildlingData.Monster.Records
                .Where(r => (r.Habitat & mask) != 0)
                .FirstOrDefault(r => state.CodexState(r.MonsterId) == CodexState.Unknown);
            if (target != null)
                state.SetCodex(target.MonsterId, CodexState.Sighted);
        }
    }

    /// <summary>전투가 끝난 뒤의 화면이다.</summary>
    public sealed class BattleResultScreen : Screen
    {
        private readonly BattleReport _battle;
        private readonly StageTable.Record _stage;
        private readonly GrantReport _report;
        private readonly bool _firstClear;
        private readonly List<RegionTable.Record> _opened;

        public BattleResultScreen(BattleReport battle, StageTable.Record stage,
                                  GrantReport report, bool firstClear,
                                  List<RegionTable.Record> opened)
        {
            _battle = battle;
            _stage = stage;
            _report = report;
            _firstClear = firstClear;
            _opened = opened;
        }

        public override string Title => _battle.PartyWon ? "승리" : "패배";

        public override void Build(Transform root, App app)
        {
            var column = Ui.Scroll(root);

            var head = Ui.Item(column, 70f);
            Ui.Label(head.transform,
                     $"{_battle.Turns}턴"
                     + (_battle.DecidedByHealth ? " · 체력 비율로 판정" : "")
                     + (_firstClear ? " · 첫 클리어" : ""),
                     26, _battle.PartyWon ? Theme.Good : Theme.Warn);

            if (_opened.Count > 0)
            {
                App.Section(column, "새로 열린 지역");
                foreach (var region in _opened)
                {
                    var item = Ui.Item(column, 60f);
                    Ui.Label(item.transform, region.Name, 24, Theme.Accent);
                }
            }

            if (_report.Applied.Count > 0)
            {
                App.Section(column, "받은 것");
                foreach (var grant in _report.Applied)
                    App.GrantRow(column, grant);
            }

            if (_report.NewMonsters.Count > 0)
            {
                App.Section(column, "새 기록");
                foreach (string id in _report.NewMonsters)
                {
                    var row = WildlingData.Monster.FindByMonsterId(id);
                    if (row != null)
                        App.MonsterRow(column, row, "신규", null);
                }
            }

            App.Section(column, "전투 기록");
            foreach (var beat in _battle.Beats)
            {
                bool detail = beat.Text.StartsWith("  ");
                var item = Ui.Item(column, detail ? 26f : 30f);
                Ui.Label(item.transform, beat.Text, detail ? 19 : 21,
                         detail ? Theme.TextDim : Theme.Text);
            }

            var buttons = Ui.Item(column, 70f);
            var buttonRow = Ui.Row(buttons.transform, 8f);
            Ui.Button(buttonRow, "다시", () => app.Go(new BattleScreen(_stage.StageId), false),
                      Theme.PanelHigh);
            Ui.Button(buttonRow, "목록으로",
                      () => app.Go(new RegionScreen(_stage.RegionId), false), Theme.Accent);
        }
    }
}
