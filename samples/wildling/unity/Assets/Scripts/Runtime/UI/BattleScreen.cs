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
        private readonly BattleRun _run;
        private BattleReport _report;
        private RectTransform _logColumn;
        private ScrollRect _logScroll;
        private RectTransform _stage;
        private App _app;
        private int _shown;
        private static int _speedIndex;
        private readonly List<BattleCell> _partyCells = new();
        private readonly List<BattleCell> _enemyCells = new();
        private readonly List<(int At, Image Plate)> _speedButtons = new();
        private Image _losePlate;
        private Text _loseLabel;

        /// <summary>돌고 있는 재생이다. 화면을 다시 만들 때 멈춰야 두 벌이 겹치지 않는다.</summary>
        private static Coroutine _playing;

        public BattleScreen(string stageId, BattleRun run = null)
        {
            _stageId = stageId;
            _run = run ?? new BattleRun();
        }

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

            // **먼저 돌던 재생을 멈춥니다.** 코루틴은 `App` 에 붙어 있으므로 화면을 다시
            // 만들어도 저절로 멈추지 않고, 그대로 두면 두 벌이 겹쳐 흐릅니다.
            if (_playing != null)
                app.StopCoroutine(_playing);
            _playing = app.StartCoroutine(Play(stage));
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
        private static List<Combatant> BuildEnemies(StageRecord stage)
        {
            var list = new List<Combatant>();
            var wave = stage.MonsterByWaveMonsterIds ?? System.Array.Empty<MonsterRecord>();

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
                    Active = Stats.BasicFirst(skills.Where(r => r.SlotKind == SlotKind.Active))
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

        private void BuildLayout(Transform root, StageRecord stage)
        {
            var region = stage.RegionByRegionId;
            if (region != null)
            {
                var bg = Ui.Node("bg", root);
                Ui.Stretch(bg);
                // 흐르는 배경. 관전 중에도 화면이 살아 있게 합니다.
                Parallax.Attach(bg, ArtLibrary.Model(region.Background), 34f,
                                new Color(0.55f, 0.55f, 0.62f, 1f));
            }

            _stage = Ui.Rect(root.gameObject);

            var head = Ui.Node("head", root);
            Ui.Top(head, 64f);
            Ui.Label(head.transform,
                     $"{region?.Name ?? stage.RegionId} {stage.Index} — "
                     + Theme.Label(stage.StageKind),
                     24, Theme.OnDark, TextAnchor.MiddleCenter);

            var enemies = Ui.Node("enemies", root);
            Ui.Top(enemies, 200f, 68f);

            var party = Ui.Node("party", root);
            Ui.Top(party, 200f, 276f);

            var logBox = Ui.Node("log", root);
            var rt = Ui.Rect(logBox);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(10f, 116f);   // 아래에 연속 전투 띠가 들어갑니다
            rt.offsetMax = new Vector2(-10f, -486f);
            var panel = logBox.AddComponent<Image>();
            panel.color = new Color(0f, 0f, 0f, 0.55f);
            _logColumn = Ui.Scroll(logBox.transform, 2f, 8f);
            // **`ScrollRect` 는 `Ui.Scroll` 이 만든 안쪽 오브젝트에 있습니다.** 바깥에서
            // 찾으면 `null` 이 나오고 기록이 새 줄을 따라가지 않습니다.
            _logScroll = _logColumn.GetComponentInParent<ScrollRect>();

            // **뜨는 것들의 층은 맨 마지막에 만듭니다.** uGUI 는 자식 순서대로 그리므로
            // 나중에 만든 것이 위에 옵니다.
            var floats = Ui.Node("floats", root);
            var floatLayer = Ui.Stretch(floats);
            floats.AddComponent<CanvasGroup>().blocksRaycasts = false;

            _enemyCells.AddRange(
                BattleStage.Build(enemies.transform, _report.Enemies, -1f, floatLayer));
            _partyCells.AddRange(
                BattleStage.Build(party.transform, _report.Party, 1f, floatLayer));

            // 지금까지 몇 판을 돌았고 무엇이 쌓였는가.
            var strip = Ui.Node("strip", root);
            Ui.Bottom(strip, 34f, 72f);
            var stripPanel = strip.AddComponent<Image>();
            stripPanel.color = new Color(0f, 0f, 0f, 0.55f);
            stripPanel.raycastTarget = false;
            var stripText = Ui.Node("t", strip.transform);
            var strt = Ui.Stretch(stripText);
            strt.offsetMin = new Vector2(10f, 0f);
            strt.offsetMax = new Vector2(-10f, 0f);
            Ui.Label(stripText.transform,
                     $"자동 반복 · {_run.Rounds + 1}판째 · 승 {_run.Wins} 패 {_run.Losses}"
                     + (string.IsNullOrEmpty(_run.FellBackTo) ? "" : " · 막혀서 물러났습니다")
                     + (_run.Tally.Count > 0
                         ? "   |   " + string.Join(" · ",
                             _run.Tally.Take(3).Select(Rewards.Describe))
                         : ""),
                     18, string.IsNullOrEmpty(_run.FellBackTo) ? Theme.OnDarkDim : Theme.Warn);

            var buttons = Ui.Node("buttons", root);
            Ui.Bottom(buttons, 64f, 6f);
            var row = Ui.Row(buttons.transform, 8f);

            // **배속은 전투 중에 바뀝니다.** 그래서 다시 조립하지 않고 색만 갈아 끼웁니다 —
            // 화면을 다시 만들면 전투가 처음부터 다시 돌아갑니다.
            _speedButtons.Clear();
            foreach (var speed in WildlingData.BattleSpeed.Records.OrderBy(s => s.At))
            {
                int at = speed.At;
                var button = Ui.Button(row, speed.Label, () =>
                {
                    _speedIndex = at;
                    PaintSpeed();
                }, Theme.PanelHigh, 20);
                _speedButtons.Add((at, button.targetGraphic as Image));
            }
            PaintSpeed();

            Ui.Button(row, "건너뛰기", () =>
            {
                while (_shown < _report.Beats.Count)
                    AppendLine();
            }, Theme.PanelHigh, 20);

            // **여기서 화면을 다시 만들면 전투가 처음부터 다시 돕니다.** 글자와 껍데기만
            // 갈아 끼웁니다.
            var lose = Ui.Button(row, "", () =>
            {
                BattleRun.StopOnLose = !BattleRun.StopOnLose;
                PaintLose();
                _app.Toast(BattleRun.StopOnLose
                    ? "지면 멈춥니다."
                    : "지면 깬 자리로 물러나 계속 돕니다.");
            }, Theme.PanelHigh, 18);

            _losePlate = lose.targetGraphic as Image;
            _loseLabel = lose.GetComponentInChildren<Text>();
            PaintLose();

            Ui.Button(row, "정지", () =>
            {
                _run.Stopped = true;
                _app.Toast("이 판이 끝나면 멈춥니다.");
            }, Theme.Warn, 20);
        }

        // ------------------------------------------------------------ 재생

        /// <summary>패배 시 어떻게 하는지를 버튼에 적는다.</summary>
        private void PaintLose()
        {
            if (_loseLabel != null)
                _loseLabel.text = BattleRun.StopOnLose ? "패배 시 중단" : "패배 시 반복";
            if (_losePlate != null)
            {
                _losePlate.sprite = BattleRun.StopOnLose ? Skin.ButtonWarn : Skin.Button;
                _losePlate.color = Skin.TintFor(_losePlate.sprite);
            }
        }

        /// <summary>고른 배속만 밝게 둔다.</summary>
        private void PaintSpeed()
        {
            foreach (var (at, plate) in _speedButtons)
            {
                if (plate == null)
                    continue;
                // **색이 아니라 껍데기를 바꿉니다.** 회색 판에 초록을 곱하면 탁해집니다.
                plate.sprite = at == _speedIndex ? Skin.ButtonAccent : Skin.Button;
                plate.color = Skin.TintFor(plate.sprite);
            }
        }

        /// <summary>지금 고른 배속이다. 표의 `BattleSpeed` 가 값을 정한다.</summary>
        private static int Multiplier()
            => System.Math.Max(1, WildlingData.BattleSpeed.Records
                .FirstOrDefault(s => s.At == _speedIndex)?.Multiplier ?? 1);

        private IEnumerator Play(StageRecord stage)
        {
            while (_shown < _report.Beats.Count)
            {
                AppendLine();
                // **매 줄마다 다시 읽습니다.** 시작할 때 한 번만 읽으면 전투 중에 누른 배속이
                // 그 판에 반영되지 않습니다.
                yield return new WaitForSeconds(0.24f / Multiplier());
            }

            yield return new WaitForSeconds(0.5f / Multiplier());
            Finish(stage);
        }

        private void AppendLine()
        {
            if (_shown >= _report.Beats.Count || _logColumn == null)
                return;

            bool atBottom = AtBottom();

            var beat = _report.Beats[_shown++];
            bool detail = beat.Text.StartsWith("  ");

            var item = Ui.Item(_logColumn, detail ? 28f : 32f);
            var line = Ui.Label(item.transform, BattleLog.Rich(_report, beat),
                                detail ? 20 : 22, Theme.OnDark);
            line.supportRichText = true;

            Fill(_partyCells, _report.Party, beat.PartyHp);
            Fill(_enemyCells, _report.Enemies, beat.EnemyHp);
            Perform(beat);

            if (atBottom && _logScroll != null)
            {
                // 층이 새 줄의 높이를 반영한 뒤라야 맨 아래가 맨 아래입니다.
                Canvas.ForceUpdateCanvases();
                LayoutRebuilder.ForceRebuildLayoutImmediate(_logColumn);
                _logScroll.verticalNormalizedPosition = 0f;
            }
        }

        /// <summary>
        /// 지금 맨 아래를 보고 있는가.
        /// </summary>
        /// <remarks>
        /// **위로 올려 둔 것을 끌어내리지 않습니다.** 지난 줄을 읽고 있는데 새 줄마다 아래로
        /// 튀면 읽을 수 없습니다. 내용이 화면보다 짧으면 그것도 맨 아래로 봅니다.
        /// </remarks>
        private bool AtBottom()
        {
            if (_logScroll == null || _logScroll.content == null || _logScroll.viewport == null)
                return true;
            if (_logScroll.content.rect.height <= _logScroll.viewport.rect.height)
                return true;
            return _logScroll.verticalNormalizedPosition <= 0.02f;
        }

        /// <summary>그 박자의 체력으로 막대를 채운다.</summary>
        private static void Fill(List<BattleCell> cells, List<Combatant> side, int[] hp)
        {
            if (hp is null)
                return;

            for (int i = 0; i < cells.Count && i < side.Count && i < hp.Length; i++)
            {
                var cell = cells[i];
                if (cell == null || cell.HpFill == null)
                    continue;

                float ratio = side[i].MaxHp <= 0 ? 0f : Mathf.Clamp01(hp[i] / (float)side[i].MaxHp);
                var rt = (RectTransform)cell.HpFill.transform;
                rt.anchorMax = new Vector2(ratio, 1f);
                cell.HpFill.color = hp[i] > 0 ? Theme.Good : Theme.Warn;
                cell.SetDown(hp[i] <= 0);
            }
        }

        /// <summary>
        /// 그 박자를 눈에 보이게 한다.
        /// </summary>
        /// <remarks>
        /// 움직인 쪽이 한 걸음 나가고, 맞은 쪽이 흔들리며 번쩍이고, 숫자가 떠오릅니다.
        /// **어느 칸인지는 기록이 들고 있습니다** — 화면이 글을 다시 읽어 알아내지 않습니다.
        /// </remarks>
        private void Perform(BattleReport.Beat beat)
        {
            var actor = CellAt(beat.ActorIsEnemy, beat.ActorIndex);
            var target = CellAt(beat.TargetIsEnemy, beat.TargetIndex);

            if (beat.Kind == BeatKind.Act)
            {
                // 차례는 테두리로 알립니다 — 움직이는 것은 그림 하나뿐입니다.
                MarkTurn(actor);
                actor?.Nudge();
                actor?.Banner(beat.Note, ArtLibrary.Icon(beat.Icon));
                return;
            }

            if (beat.Kind == BeatKind.Status && actor == target)
                MarkTurn(actor);

            // 「패스」처럼 스스로에게 일어난 것은 그 칸 위에 적습니다.
            if (beat.Kind == BeatKind.Line)
            {
                MarkTurn(actor);
                if (!string.IsNullOrEmpty(beat.Note))
                    actor?.Float(beat.Note, Theme.TextDim, 22, false);
                return;
            }

            if (target == null)
                return;

            target.Play(beat.Kind, beat.Amount, beat.Crit, true, beat.Affinity);

            if (!string.IsNullOrEmpty(beat.Note))
                target.Banner(beat.Note, null);

            // 치명타는 화면 전체가 흔들리고 별이 튑니다.
            if (beat.Crit && beat.Kind == BeatKind.Damage)
            {
                StartStageShake();
                Fx.Sparks(_app.Effects, CellAnchor(target), new Color(1f, 0.82f, 0.30f), 8, 150f);
                Fx.Flash(_app.Effects, new Color(1f, 0.95f, 0.75f), 0.16f);
            }

            if (beat.Kind == BeatKind.Down)
                Fx.Burst(_app.Effects, CellAnchor(target), new Color(1f, 0.42f, 0.34f), 220f);
        }

        /// <summary>화면 전체를 짧게 흔든다. 치명타에만 쓴다.</summary>
        private void StartStageShake()
        {
            if (_stage == null || _app == null)
                return;
            _app.StartCoroutine(StageShake(_stage));
        }

        private static IEnumerator StageShake(RectTransform stage)
        {
            Vector2 home = stage.anchoredPosition;
            float time = 0f;
            const float span = 0.20f;

            while (time < span && stage != null)
            {
                time += Time.deltaTime;
                float fade = 1f - Mathf.Clamp01(time / span);
                stage.anchoredPosition = home + new Vector2(Mathf.Sin(time * 74f) * 9f * fade, 0f);
                yield return null;
            }

            if (stage != null)
                stage.anchoredPosition = home;
        }

        /// <summary>그 칸 하나만 차례로 표시한다.</summary>
        private void MarkTurn(BattleCell active)
        {
            foreach (var cell in _partyCells)
                cell.SetTurn(cell == active);
            foreach (var cell in _enemyCells)
                cell.SetTurn(cell == active);
        }

        /// <summary>그 칸의 가운데가 효과 층에서 어디인가.</summary>
        private Vector2 CellAnchor(BattleCell cell)
        {
            if (cell == null || cell.Actor == null || _app == null || _app.Effects == null)
                return Vector2.zero;
            return ((RectTransform)_app.Effects).InverseTransformPoint(cell.Actor.position);
        }

        private BattleCell CellAt(bool isEnemy, int index)
        {
            var list = isEnemy ? _enemyCells : _partyCells;
            return index >= 0 && index < list.Count ? list[index] : null;
        }

        /// <summary>
        /// 결과를 상태에 반영한다.
        /// </summary>
        /// <remarks>
        /// **여기가 승리 하나로 표 여섯이 움직이는 자리입니다** — `StageReward` 의 첫 클리어와
        /// 반복 보상, `RewardEntry` 의 변종 지급, `CodexConst` 의 전투 관측, 관측 스테이지의
        /// 목격 전이, 그리고 `RequirementGroup` 을 다시 확인해 열리는 지역.
        /// </remarks>
        private void Finish(StageRecord stage)
        {
            var state = _app.State;
            var grants = new List<Grant>();
            var opened = new List<RegionRecord>();
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

            _run.Rounds++;
            if (_report.PartyWon)
            {
                _run.Wins++;
                if (firstClear)
                {
                    Fx.Flash(_app.Effects, Color.white, 0.30f);
                    Fx.Shout(_app.Effects, "클리어!", Theme.Accent, 66);
                }
            }
            else
                _run.Losses++;
            _run.Add(report.Applied);
            _run.NewMonsters.AddRange(report.NewMonsters);
            _run.Opened.AddRange(opened.Select(r => r.Name));

            // **방치형이므로 이긴 동안은 계속 돕니다.** 진 판에서 멈추는 것이 기획서 9.2 의
            // 「패배 시 중단」이고, 사람이 「정지」를 누른 것도 여기서 걸립니다.
            bool carryOn = !_run.Stopped
                           && (_report.PartyWon || !BattleRun.StopOnLose)
                           && _run.Rounds < 500;

            if (carryOn)
            {
                // 이겼으면 앞으로, 졌으면 깬 자리로 물러나 계속 돕니다.
                string next = _report.PartyWon
                    ? BattleRun.NextStage(state, stage)
                    : BattleRun.FallbackStage(state, stage);

                _run.FellBackTo = _report.PartyWon ? "" : next;

                // 같은 자리에서 계속 지면 그 자리가 벽입니다. 사람이 볼 수 있게 멈춥니다.
                if (!_report.PartyWon && next == stage.StageId && !firstClear
                    && _run.Losses >= 3)
                {
                    carryOn = false;
                }
                else if (!string.IsNullOrEmpty(next))
                {
                    _app.Go(new BattleScreen(next, _run), false);
                    return;
                }
            }

            _app.Go(new BattleResultScreen(_report, stage, report, firstClear, opened, _run),
                    false);
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
        private readonly StageRecord _stage;
        private readonly GrantReport _report;
        private readonly bool _firstClear;
        private readonly List<RegionRecord> _opened;
        private readonly BattleRun _run;

        public BattleResultScreen(BattleReport battle, StageRecord stage,
                                  GrantReport report, bool firstClear,
                                  List<RegionRecord> opened, BattleRun run = null)
        {
            _run = run;
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

            if (!_battle.PartyWon)
            {
                App.Section(column, "진 이유");
                foreach (string reason in Diagnose.Why(_battle))
                {
                    var line = Ui.Item(column, 40f);
                    Ui.Label(line.transform, reason, 21, Theme.Warn);
                }
            }

            if (_run != null && _run.Rounds > 1)
            {
                App.Section(column, $"연속 전투 — {_run.Rounds}판 · 승 {_run.Wins} 패 {_run.Losses}");
                foreach (var grant in _run.Tally)
                    App.GrantRow(column, grant);
            }

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
                var item = Ui.Item(column, detail ? 28f : 32f);
                var line = Ui.Label(item.transform, BattleLog.Rich(_battle, beat),
                                    detail ? 20 : 21, Theme.Text);
                line.supportRichText = true;
            }

            var buttons = Ui.Item(column, 70f);
            var buttonRow = Ui.Row(buttons.transform, 8f);
            Ui.Button(buttonRow, "다시", () => app.Go(new BattleScreen(_stage.StageId), false),
                      Theme.Accent);
            Ui.Button(buttonRow, "파티", () => app.Go(new PartyScreen()), Theme.PanelHigh);
            Ui.Button(buttonRow, "목록으로",
                      () => app.Go(new RegionScreen(_stage.RegionId), false), Theme.Accent);
        }
    }
}
