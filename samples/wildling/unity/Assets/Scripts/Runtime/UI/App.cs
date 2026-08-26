using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Wildling.Data;

namespace Wildling.Game
{
    /// <summary>화면 하나이다.</summary>
    public abstract class Screen
    {
        public abstract string Title { get; }

        /// <summary>재화 줄과 아래쪽 이동 줄을 보일 것인가.</summary>
        public virtual bool Chrome => true;

        public abstract void Build(Transform root, App app);
    }

    /// <summary>
    /// 화면을 갈아 끼우고 상태를 들고 있는 자리이다.
    /// </summary>
    /// <remarks>
    /// **화면은 매번 다시 조립합니다.** 상태가 바뀌면 부분 갱신 대신 그 화면을 지우고 다시
    /// 만듭니다 — 표에서 읽은 값이 화면에 그대로 나오는지 보이는 것이 이 샘플의 목적이므로,
    /// 값이 어디서 왔는지 흐려지는 갱신 경로를 두지 않습니다.
    /// </remarks>
    public sealed class App : MonoBehaviour
    {
        public static App Current { get; private set; }

        public GameState State;

        private Transform _content;
        private Transform _chrome;
        private Transform _toast;

        /// <summary>터지는 것들이 그려지는 자리이다. 언제나 맨 위이다.</summary>
        public Transform Effects { get; private set; }
        private Screen _screen;
        private readonly List<Screen> _back = new();

        private float _toastUntil;

        public event Action Rebuilt;

        // ------------------------------------------------------------ 만들기

        public void Attach(Canvas canvas)
        {
            Current = this;

            var root = canvas.transform;
            Ui.Panel(root, Theme.Background, "background");

            var body = Ui.Node("body", root);
            Ui.Stretch(body);
            _content = body.transform;

            var chrome = Ui.Node("chrome", root);
            Ui.Stretch(chrome);
            _chrome = chrome.transform;

            var effects = Ui.Node("effects", root);
            Ui.Stretch(effects);
            effects.AddComponent<CanvasGroup>().blocksRaycasts = false;
            Effects = effects.transform;

            var toast = Ui.Node("toast", root);
            Ui.Stretch(toast);
            _toast = toast.transform;
        }

        // ------------------------------------------------------------ 이동

        public void Go(Screen screen, bool remember = true)
        {
            if (remember && _screen != null)
                _back.Add(_screen);
            _screen = screen;
            Rebuild();
        }

        public void Back()
        {
            if (_back.Count == 0)
            {
                Go(new HomeScreen(), false);
                return;
            }
            var previous = _back[^1];
            _back.RemoveAt(_back.Count - 1);
            Go(previous, false);
        }

        public void Rebuild()
        {
            if (_screen is null)
                return;

            Ui.Clear(_content);
            Ui.Clear(_chrome);

            float top = _screen.Chrome ? 150f : 0f;
            float bottom = _screen.Chrome ? 130f : 0f;

            var area = Ui.Node("area", _content);
            var rt = Ui.Rect(area);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(0f, bottom);
            rt.offsetMax = new Vector2(0f, -top);

            _screen.Build(area.transform, this);

            if (_screen.Chrome)
            {
                BuildTopBar();
                BuildNavBar();
            }

            Rebuilt?.Invoke();
        }

        private void BuildTopBar()
        {
            var bar = Ui.Node("topbar", _chrome);
            Ui.Top(bar, 150f);
            Ui.Panel(bar.transform, Theme.Panel);

            var title = Ui.Node("title", bar.transform);
            Ui.Top(title, 48f, 8f);
            Ui.Label(title.transform, _screen.Title, 30, Theme.Text, TextAnchor.MiddleCenter);

            var row = Ui.Node("currencies", bar.transform);
            Ui.Bottom(row, 74f, 10f);
            var line = Ui.Row(row.transform, 6f, 10f);

            foreach (var currency in WildlingData.Currency.Records)
            {
                var cell = Ui.Node(currency.CurrencyId, line);
                var icon = Ui.Node("i", cell.transform);
                var irt = Ui.Rect(icon);
                irt.anchorMin = new Vector2(0f, 0.5f);
                irt.anchorMax = new Vector2(0f, 0.5f);
                irt.pivot = new Vector2(0f, 0.5f);
                irt.sizeDelta = new Vector2(48f, 48f);
                irt.anchoredPosition = new Vector2(2f, 0f);
                Ui.Icon(icon.transform, ArtLibrary.Icon(currency.Icon));

                var value = Ui.Node("v", cell.transform);
                var vrt = Ui.Stretch(value);
                vrt.offsetMin = new Vector2(54f, 0f);
                Ui.Label(value.transform, Numbers.Short(State.Currency(currency.CurrencyId)),
                         22, Theme.Text);
            }
        }

        private void BuildNavBar()
        {
            var bar = Ui.Node("navbar", _chrome);
            Ui.Bottom(bar, 130f);
            Ui.Panel(bar.transform, Theme.Panel);

            var row = Ui.Row(bar.transform, 6f, 10f);

            Tab("홈", () => new HomeScreen());
            Tab("기록부", () => new CodexScreen());
            Tab("파티", () => new PartyScreen());
            Tab("지역", () => new RegionScreen());
            Tab("상점", () => new ShopScreen());

            void Tab(string text, Func<Screen> make)
            {
                bool here = _screen.Title == make().Title;
                Ui.Button(row, text, () =>
                {
                    _back.Clear();
                    Go(make(), false);
                }, here ? Theme.Accent : Theme.PanelHigh, 22);
            }
        }

        // ------------------------------------------------------------ 알림

        public void Toast(string message)
        {
            Ui.Clear(_toast);
            _toastUntil = Time.unscaledTime + 2.6f;

            var box = Ui.Node("box", _toast);
            var rt = Ui.Rect(box);
            rt.anchorMin = new Vector2(0.5f, 0f);
            rt.anchorMax = new Vector2(0.5f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.sizeDelta = new Vector2(640f, 64f);
            rt.anchoredPosition = new Vector2(0f, 160f);

            var image = box.AddComponent<Image>();
            image.color = new Color(0f, 0f, 0f, 0.82f);
            Ui.Label(box.transform, message, 22, Theme.Text, TextAnchor.MiddleCenter);
        }

        private void Update()
        {
            if (_toastUntil > 0f && Time.unscaledTime > _toastUntil)
            {
                _toastUntil = 0f;
                Ui.Clear(_toast);
            }
        }

        // ------------------------------------------------------------ 공용 조각

        /// <summary>와일드링 한 줄이다. 목록마다 같은 모양으로 나온다.</summary>
        public static GameObject MonsterRow(Transform parent, MonsterTable.Record row,
                                            string right, Action onClick, float height = 112f)
        {
            var item = Ui.Item(parent, height);
            if (onClick != null)
                Ui.Button(item.transform, "", onClick, Theme.Panel);
            else
                Ui.Panel(item.transform, Theme.Panel);

            var icon = Ui.Node("icon", item.transform);
            var irt = Ui.Rect(icon);
            irt.anchorMin = new Vector2(0f, 0.5f);
            irt.anchorMax = new Vector2(0f, 0.5f);
            irt.pivot = new Vector2(0f, 0.5f);
            irt.sizeDelta = new Vector2(96f, 96f);
            irt.anchoredPosition = new Vector2(8f, 0f);
            Ui.FramedIcon(icon.transform, ArtLibrary.Icon(row.Icon), row.Grade);

            var name = Ui.Node("name", item.transform);
            var nrt = Ui.Rect(name);
            nrt.anchorMin = new Vector2(0f, 1f);
            nrt.anchorMax = new Vector2(1f, 1f);
            nrt.pivot = new Vector2(0.5f, 1f);
            nrt.offsetMin = new Vector2(112f, -46f);
            nrt.offsetMax = new Vector2(-12f, -12f);
            Ui.Label(name.transform, row.Name, 26);

            var tags = Ui.Node("tags", item.transform);
            var trt = Ui.Rect(tags);
            trt.anchorMin = new Vector2(0f, 0f);
            trt.anchorMax = new Vector2(1f, 0f);
            trt.pivot = new Vector2(0.5f, 0f);
            trt.offsetMin = new Vector2(112f, 12f);
            trt.offsetMax = new Vector2(-12f, 46f);
            Ui.Label(tags.transform,
                     $"{Theme.Label(row.Element)} · {Theme.Label(row.Grade)} · "
                     + $"{Theme.Label(row.Role)} · {row.Stage}단",
                     20, Theme.TextDim);

            if (!string.IsNullOrEmpty(right))
            {
                var value = Ui.Node("right", item.transform);
                var vrt = Ui.Rect(value);
                vrt.anchorMin = new Vector2(1f, 0f);
                vrt.anchorMax = new Vector2(1f, 1f);
                vrt.pivot = new Vector2(1f, 0.5f);
                vrt.sizeDelta = new Vector2(200f, 0f);
                vrt.anchoredPosition = new Vector2(-12f, 0f);
                Ui.Label(value.transform, right, 22, Theme.Accent, TextAnchor.MiddleRight);
            }

            // 속성 색 띠. 아이콘의 색과 같은 규칙이므로 어긋나면 보인다.
            var stripe = Ui.Node("stripe", item.transform);
            var srt = Ui.Rect(stripe);
            srt.anchorMin = new Vector2(0f, 0f);
            srt.anchorMax = new Vector2(0f, 1f);
            srt.pivot = new Vector2(0f, 0.5f);
            srt.sizeDelta = new Vector2(6f, 0f);
            var strip = stripe.AddComponent<Image>();
            strip.color = Theme.Element(row.Element);
            strip.raycastTarget = false;

            return item;
        }

        /// <summary>목록의 구역 제목이다.</summary>
        public static void Section(Transform parent, string text)
        {
            var item = Ui.Item(parent, 46f);
            Ui.Label(item.transform, text, 22, Theme.TextDim);
        }

        /// <summary>지급물을 아이콘과 함께 한 줄로 적는다.</summary>
        public static void GrantRow(Transform parent, Grant grant)
        {
            var item = Ui.Item(parent, 64f);
            Ui.Panel(item.transform, Theme.Panel);

            var icon = Ui.Node("icon", item.transform);
            var irt = Ui.Rect(icon);
            irt.anchorMin = new Vector2(0f, 0.5f);
            irt.anchorMax = new Vector2(0f, 0.5f);
            irt.pivot = new Vector2(0f, 0.5f);
            irt.sizeDelta = new Vector2(52f, 52f);
            irt.anchoredPosition = new Vector2(8f, 0f);
            Ui.Icon(icon.transform, ArtLibrary.Icon(Rewards.IconOf(grant)));

            var text = Ui.Node("text", item.transform);
            var trt = Ui.Stretch(text);
            trt.offsetMin = new Vector2(68f, 0f);
            trt.offsetMax = new Vector2(-12f, 0f);
            Ui.Label(text.transform, Rewards.Describe(grant), 22);
        }
    }
}
