using System.Linq;
using UnityEngine;
using Wildling.Data;

namespace Wildling.Game
{
    /// <summary>
    /// 파티 편성이다.
    /// </summary>
    /// <remarks>
    /// **열의 이름과 역할별 제약이 전부 `PartyConst` 에 있습니다.** 「선봉은 앞열」이 코드에
    /// 고정되어 있지 않으므로, 배치 규칙을 바꾸는 것이 상수셋 수정으로 끝납니다.
    /// </remarks>
    public sealed class PartyScreen : Screen
    {
        private readonly int _pickColumn;

        public PartyScreen(int pickColumn = -1) => _pickColumn = pickColumn;

        public override string Title => "파티";

        public override void Build(Transform root, App app)
        {
            var state = app.State;
            var column = Ui.Scroll(root);

            if (_pickColumn >= 0)
            {
                BuildPicker(column, app);
                return;
            }

            // 저장 슬롯 고르기
            var tabs = Ui.Item(column, 62f);
            var tabRow = Ui.Row(tabs.transform, 8f);
            for (int i = 0; i < PartyConst.SavedParties; i++)
            {
                int index = i;
                Ui.Button(tabRow, $"파티 {i + 1}", () =>
                {
                    state.ActiveParty = index;
                    SaveStore.Save(state);
                    app.Rebuild();
                }, state.ActiveParty == index ? Theme.Accent : Theme.PanelHigh, 22);
            }

            var slots = state.Party(state.ActiveParty);

            for (int i = 0; i < slots.Length; i++)
            {
                int index = i;
                string columnName = index < PartyConst.ColumnNames.Length
                    ? PartyConst.ColumnNames[index]
                    : $"{index + 1}";

                var allowed = new[] { Role.Vanguard, Role.Breaker, Role.Warden, Role.Tuner }
                    .Where(r => GameState.ColumnAllows(r, index))
                    .Select(Theme.Label);

                App.Section(column, $"{ColumnLabel(columnName)} — {string.Join(" · ", allowed)}");

                var owned = state.Find(slots[index]);
                if (owned?.Row != null)
                {
                    var stats = Stats.Compute(owned.Row, owned.Level,
                                              state.Resonance(owned.SpeciesId));
                    App.MonsterRow(column, owned.Row,
                                   $"Lv {owned.Level}\n체력 {stats.Hp}",
                                   () => app.Go(new PartyScreen(index)));
                }
                else
                {
                    var empty = Ui.Item(column, 84f);
                    Ui.Button(empty.transform, "비어 있습니다 — 고르기",
                              () => app.Go(new PartyScreen(index)));
                }
            }

            var actions = Ui.Item(column, 70f);
            var actionRow = Ui.Row(actions.transform, 8f);
            Ui.Button(actionRow, "자동 편성", () =>
            {
                for (int i = 0; i < slots.Length; i++)
                    slots[i] = 0;
                state.AutoFillParty();
                SaveStore.Save(state);
                app.Rebuild();
            }, Theme.Accent);

            App.Section(column, $"동행 {state.All.Count}마리");
            foreach (var owned in state.All.OrderByDescending(o => o.Level))
            {
                if (owned.Row is null)
                    continue;
                bool inParty = slots.Contains(owned.Uid);
                App.MonsterRow(column, owned.Row,
                               inParty ? "편성" : $"Lv {owned.Level}",
                               () => app.Go(new MonsterScreen(owned.Uid)));
            }
        }

        // ------------------------------------------------------------ 고르기

        private void BuildPicker(Transform column, App app)
        {
            var state = app.State;
            string columnName = _pickColumn < PartyConst.ColumnNames.Length
                ? PartyConst.ColumnNames[_pickColumn]
                : $"{_pickColumn + 1}";

            App.Section(column, $"{ColumnLabel(columnName)} 에 세울 개체");

            var candidates = state.All
                .Where(o => o.Row != null && GameState.ColumnAllows(o.Row.Role, _pickColumn))
                .OrderByDescending(o => o.Level)
                .ToList();

            var clear = Ui.Item(column, 66f);
            Ui.Button(clear.transform, "비우기", () =>
            {
                state.SetPartySlot(state.ActiveParty, _pickColumn, 0);
                SaveStore.Save(state);
                app.Go(new PartyScreen(), false);
            });

            if (candidates.Count == 0)
            {
                var none = Ui.Item(column, 60f);
                Ui.Label(none.transform, "이 열에 설 수 있는 개체가 없습니다.", 22, Theme.Warn);
                return;
            }

            foreach (var owned in candidates)
            {
                var stats = Stats.Compute(owned.Row, owned.Level,
                                          state.Resonance(owned.SpeciesId));
                App.MonsterRow(column, owned.Row,
                               $"Lv {owned.Level}\n체력 {stats.Hp} · 공격 {stats.Attack}",
                               () =>
                               {
                                   state.SetPartySlot(state.ActiveParty, _pickColumn, owned.Uid);
                                   SaveStore.Save(state);
                                   app.Go(new PartyScreen(), false);
                               });
            }
        }

        /// <summary>`PartyConst.ColumnNames` 는 영문 식별자이므로 표시할 이름으로 바꾼다.</summary>
        public static string ColumnLabel(string name) => name switch
        {
            "Front" => "앞열",
            "Middle" => "중열",
            "Back" => "뒷열",
            _ => name,
        };
    }
}
