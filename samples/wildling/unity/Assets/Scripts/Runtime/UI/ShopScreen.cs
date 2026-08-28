using System.Linq;
using UnityEngine;
using Wildling.Data;

namespace Wildling.Game
{
    /// <summary>
    /// 상점 · 의뢰 · 광고 보상이다.
    /// </summary>
    /// <remarks>
    /// **범위가 여기까지인 이유가 있습니다.** 결제와 광고 SDK 는 이 샘플이 증명하려는 것이
    /// 아니므로 붙이지 않고, 표를 읽어 화면에 그리는 데까지 합니다 — 그래야 테이블 40개 중
    /// 화면에 닿지 않는 것이 없습니다.
    ///
    /// **`ShopSlot` 은 원본이 세 벌입니다.** 상시 · 패키지 · 시즌이 각각 다른 시트에 있고
    /// 한 테이블로 합쳐집니다. 그래서 이 화면은 `shop_id` 로만 갈라 보면 됩니다.
    /// </remarks>
    public sealed class ShopScreen : Screen
    {
        public override string Title => "상점";

        public override void Build(Transform root, App app)
        {
            var state = app.State;
            var column = Ui.Scroll(root);

            foreach (var shop in WildlingData.Shop.Records)
            {
                App.Section(column, $"{shop.Name} — {shop.RefreshHours}시간마다 갱신");

                var slots = WildlingData.ShopSlot.Records
                    .Where(s => s.ShopId == shop.ShopId)
                    .OrderBy(s => s.SlotIndex)
                    .ToList();

                if (slots.Count == 0)
                {
                    var empty = Ui.Item(column, 44f);
                    Ui.Label(empty.transform, "판매 항목이 없습니다.", 20, Theme.TextDim);
                }

                foreach (var slot in slots)
                    SlotRow(column, app, slot);
            }

            // ---------------------------------------------------------- 패키지
            App.Section(column, "패키지");
            foreach (var package in WildlingData.Package.Records.OrderBy(p => p.SortOrder))
            {
                var item = Ui.Item(column, 84f);
                Ui.Panel(item.transform, Theme.Panel);
                var text = Ui.Node("t", item.transform);
                var trt = Ui.Stretch(text);
                trt.offsetMin = new Vector2(14f, 6f);
                trt.offsetMax = new Vector2(-14f, -6f);
                var lines = Ui.Column(text.transform, 2f);

                var head = Ui.Item(lines, 32f);
                Ui.Label(head.transform, $"{package.Name}   {package.PriceDisplay}", 23);

                var body = Ui.Item(lines, 28f);
                Ui.Label(body.transform,
                         string.Join(" · ", Rewards.Entries(package.RewardGroupId)
                             .Select(e => Rewards.Describe(Rewards.ToGrant(e.Reward)))),
                         19, Theme.TextDim);
            }

            // ---------------------------------------------------------- 패스
            App.Section(column, "탐사 지원");
            foreach (var benefit in WildlingData.PassBenefit.Records)
            {
                var item = Ui.Item(column, 62f);
                Ui.Panel(item.transform, Theme.Panel);
                var text = Ui.Node("t", item.transform);
                var trt = Ui.Stretch(text);
                trt.offsetMin = new Vector2(14f, 0f);
                trt.offsetMax = new Vector2(-14f, 0f);
                Ui.Label(text.transform,
                         $"{benefit.Name}"
                         + (benefit.IdleHoursBonus > 0
                             ? $" · 방치 +{benefit.IdleHoursBonus}시간"
                             : "")
                         + (benefit.AdFree ? " · 광고 없음" : ""),
                         21);
            }

            // ---------------------------------------------------------- 광고
            App.Section(column, "광고 보상");
            foreach (var ad in WildlingData.AdReward.Records)
            {
                var item = Ui.Item(column, 70f);
                Ui.Button(item.transform,
                          $"{ad.Name} 받기   하루 {ad.DailyLimit}회", () =>
                          {
                              // 광고 SDK 가 없으므로 그 자리에서 지급합니다.
                              var grants = Rewards.Certain(ad.RewardGroupId);
                              var report = state.Apply(grants);
                              SaveStore.Save(state);
                              app.Toast(report.Lines.Count > 0
                                  ? string.Join(" · ", report.Lines)
                                  : "받을 것이 없습니다.");
                              app.Rebuild();
                          });
            }

            // ---------------------------------------------------------- 의뢰
            App.Section(column, "의뢰");
            foreach (var mission in WildlingData.Mission.Records
                         .OrderBy(m => m.Cycle).ThenBy(m => m.MissionId))
            {
                var item = Ui.Item(column, 80f);
                Ui.Panel(item.transform, Theme.Panel);
                var text = Ui.Node("t", item.transform);
                var trt = Ui.Stretch(text);
                trt.offsetMin = new Vector2(14f, 4f);
                trt.offsetMax = new Vector2(-14f, -4f);
                var lines = Ui.Column(text.transform, 2f);

                var head = Ui.Item(lines, 32f);
                Ui.Label(head.transform,
                         $"{(mission.Cycle == MissionCycle.Daily ? "일일" : "주간")}  "
                         + $"{mission.Name}   {mission.GoalCount}회", 22);

                var body = Ui.Item(lines, 26f);
                Ui.Label(body.transform,
                         string.Join(" · ", Rewards.Entries(mission.RewardGroupId)
                             .Select(e => Rewards.Describe(Rewards.ToGrant(e.Reward)))),
                         19, Theme.Accent);
            }
        }

        private static void SlotRow(Transform column, App app, ShopSlotRecord slot)
        {
            var state = app.State;
            var cost = slot.Cost;
            var currency = WildlingData.Currency.FindByCurrencyId(cost.CurrencyId);
            bool affordable = state.Currency(cost.CurrencyId) >= cost.Amount;

            var item = Ui.Item(column, 84f);
            Ui.Button(item.transform, "", () =>
            {
                if (!state.SpendCurrency(cost.CurrencyId, cost.Amount))
                {
                    app.Toast($"{Korean.Ga(currency?.Name ?? cost.CurrencyId)} 모자랍니다.");
                    return;
                }
                var report = state.Apply(Rewards.Certain(slot.RewardGroupId));
                SaveStore.Save(state);
                app.Toast(report.Lines.Count > 0
                    ? string.Join(" · ", report.Lines)
                    : "샀습니다.");
                app.Rebuild();
            }, Theme.Panel);

            // 파는 것의 아이콘. `RewardEntry` 의 변종이 무엇을 가리키는지에 따라 갈립니다.
            var firstEntry = Rewards.Entries(slot.RewardGroupId).FirstOrDefault();
            if (firstEntry != null)
            {
                var icon = Ui.Node("icon", item.transform);
                var irt = Ui.Rect(icon);
                irt.anchorMin = new Vector2(0f, 0.5f);
                irt.anchorMax = new Vector2(0f, 0.5f);
                irt.pivot = new Vector2(0f, 0.5f);
                irt.sizeDelta = new Vector2(60f, 60f);
                irt.anchoredPosition = new Vector2(10f, 0f);
                Ui.Icon(icon.transform,
                        ArtLibrary.Icon(Rewards.IconOf(Rewards.ToGrant(firstEntry.Reward))));
            }

            var text = Ui.Node("t", item.transform);
            var trt = Ui.Stretch(text);
            trt.offsetMin = new Vector2(80f, 6f);
            trt.offsetMax = new Vector2(-180f, -6f);
            var lines = Ui.Column(text.transform, 2f);

            var head = Ui.Item(lines, 32f);
            Ui.Label(head.transform,
                     string.Join(" · ", Rewards.Entries(slot.RewardGroupId)
                         .Select(e => Rewards.Describe(Rewards.ToGrant(e.Reward)))),
                     22);

            var body = Ui.Item(lines, 26f);
            Ui.Label(body.transform, $"재고 {slot.Stock}", 19, Theme.TextDim);

            var price = Ui.Node("price", item.transform);
            var prt = Ui.Rect(price);
            prt.anchorMin = new Vector2(1f, 0.5f);
            prt.anchorMax = new Vector2(1f, 0.5f);
            prt.pivot = new Vector2(1f, 0.5f);
            prt.sizeDelta = new Vector2(170f, 60f);
            prt.anchoredPosition = new Vector2(-12f, 0f);
            Ui.Label(price.transform,
                     $"{currency?.Name ?? cost.CurrencyId} {cost.Amount}",
                     22, affordable ? Theme.Accent : Theme.Warn, TextAnchor.MiddleRight);
        }
    }
}
