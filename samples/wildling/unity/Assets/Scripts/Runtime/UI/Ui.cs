using System;
using UnityEngine;
using UnityEngine.UI;

namespace Wildling.Game
{
    /// <summary>
    /// uGUI 를 코드에서 조립하는 도구이다.
    /// </summary>
    /// <remarks>
    /// **프리팹을 쓰지 않습니다.** 프리팹 YAML 의 변경은 리뷰되지 않지만 화면 조립 코드의
    /// 변경은 리뷰됩니다. 이 폴더가 적용 튜토리얼이기도 하므로,
    /// 「이 표의 이 컬럼이 이 텍스트가 됩니다」가 코드 한 줄로 보이는 쪽이 낫습니다.
    ///
    /// 글꼴은 레거시 `UnityEngine.UI.Text` 입니다. TextMeshPro 는 글꼴 애셋을 에디터에서
    /// 만들어 두어야 하고, 그 단계가 「클론하고 바로 빌드」를 깨뜨립니다.
    /// </remarks>
    public static class Ui
    {
        public const float Width = 720f;
        public const float Height = 1280f;

        public static GameObject Node(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go;
        }

        public static RectTransform Rect(GameObject go) => (RectTransform)go.transform;

        /// <summary>부모를 가득 채운다.</summary>
        public static RectTransform Stretch(GameObject go, float pad = 0f)
        {
            var rt = Rect(go);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(pad, pad);
            rt.offsetMax = new Vector2(-pad, -pad);
            return rt;
        }

        /// <summary>위쪽에서 <paramref name="height"/> 만큼을 차지한다.</summary>
        public static RectTransform Top(GameObject go, float height, float inset = 0f)
        {
            var rt = Rect(go);
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.offsetMin = new Vector2(0f, -height - inset);
            rt.offsetMax = new Vector2(0f, -inset);
            return rt;
        }

        /// <summary>아래쪽에서 <paramref name="height"/> 만큼을 차지한다.</summary>
        public static RectTransform Bottom(GameObject go, float height, float inset = 0f)
        {
            var rt = Rect(go);
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.offsetMin = new Vector2(0f, inset);
            rt.offsetMax = new Vector2(0f, height + inset);
            return rt;
        }

        public static Image Panel(Transform parent, Color color, string name = "panel")
        {
            var go = Node(name, parent);
            var image = go.AddComponent<Image>();
            image.color = color;
            Stretch(go);
            return image;
        }

        public static Text Label(Transform parent, string text, int size = 24,
                                 Color? color = null,
                                 TextAnchor anchor = TextAnchor.MiddleLeft)
        {
            var go = Node("label", parent);
            var label = go.AddComponent<Text>();
            label.font = Theme.Font;
            label.fontSize = size;
            label.text = text;
            label.color = color ?? Theme.Text;
            label.alignment = anchor;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Truncate;
            label.raycastTarget = false;
            Stretch(go);
            return label;
        }

        public static Image Icon(Transform parent, Sprite sprite, string name = "icon")
        {
            var go = Node(name, parent);
            var image = go.AddComponent<Image>();
            image.sprite = sprite;
            image.preserveAspect = true;
            image.color = sprite is null ? new Color(1f, 1f, 1f, 0.12f) : Color.white;
            image.raycastTarget = false;
            Stretch(go);
            return image;
        }

        public static Button Button(Transform parent, string text, Action onClick,
                                    Color? tint = null, int size = 24)
        {
            var go = Node("button", parent);
            var image = go.AddComponent<Image>();
            image.color = tint ?? Theme.PanelHigh;

            var button = go.AddComponent<Button>();
            button.targetGraphic = image;
            var colors = button.colors;
            colors.highlightedColor = new Color(1.15f, 1.15f, 1.15f, 1f);
            colors.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
            colors.disabledColor = new Color(1f, 1f, 1f, 0.35f);
            button.colors = colors;

            if (onClick != null)
                button.onClick.AddListener(() => onClick());

            Label(go.transform, text, size, Theme.Text, TextAnchor.MiddleCenter);
            Stretch(go);
            return button;
        }

        /// <summary>세로로 쌓이는 목록이다. 낸 것이 항목이 붙을 자리이다.</summary>
        public static RectTransform Column(Transform parent, float spacing = 8f,
                                           float pad = 0f, bool expand = false)
        {
            var go = Node("column", parent);
            var layout = go.AddComponent<VerticalLayoutGroup>();
            layout.spacing = spacing;
            layout.padding = new RectOffset((int)pad, (int)pad, (int)pad, (int)pad);
            layout.childControlWidth = true;
            // **높이를 층이 정하게 둡니다.** 이것이 꺼져 있으면 `LayoutElement` 의 높이가
            // 무시되고 항목이 `RectTransform` 의 기본 크기(100)로 놓입니다.
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = expand;
            return Stretch(go);
        }

        /// <summary>가로로 놓이는 줄이다.</summary>
        public static RectTransform Row(Transform parent, float spacing = 8f, float pad = 0f)
        {
            var go = Node("row", parent);
            var layout = go.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = spacing;
            layout.padding = new RectOffset((int)pad, (int)pad, (int)pad, (int)pad);
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = true;
            return Stretch(go);
        }

        /// <summary>목록의 항목 하나이다. 높이를 고정한다.</summary>
        public static GameObject Item(Transform parent, float height, string name = "item")
        {
            var go = Node(name, parent);
            var element = go.AddComponent<LayoutElement>();
            element.minHeight = height;
            element.preferredHeight = height;
            return go;
        }

        /// <summary>넘치면 위아래로 밀 수 있는 목록이다. 낸 것이 내용이 붙을 자리이다.</summary>
        public static RectTransform Scroll(Transform parent, float spacing = 8f, float pad = 8f)
        {
            var view = Node("scroll", parent);
            Stretch(view);
            var scroll = view.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Elastic;
            scroll.scrollSensitivity = 30f;

            // **`Mask` 가 아니라 `RectMask2D` 입니다.** `Mask` 는 스텐실을 쓸 그래픽이
            // 필요하고, 그 그래픽의 알파가 낮으면 알파 컷오프에 걸려 안쪽이 전부 사라집니다.
            // `RectMask2D` 는 사각형을 직접 자르므로 그래픽이 없어도 됩니다.
            view.AddComponent<RectMask2D>();

            // 빈 자리에서도 밀 수 있게 투명한 판을 깝니다. 알파가 0이어도 레이캐스트는 받습니다.
            var grab = view.AddComponent<Image>();
            grab.color = new Color(0f, 0f, 0f, 0f);

            var content = Node("content", view.transform);
            var rt = Rect(content);
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            var layout = content.AddComponent<VerticalLayoutGroup>();
            layout.spacing = spacing;
            layout.padding = new RectOffset((int)pad, (int)pad, (int)pad, (int)pad);
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            var fitter = content.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            scroll.content = rt;
            scroll.viewport = Rect(view);
            return rt;
        }

        /// <summary>가로로 채워지는 막대이다. 진척과 체력에 쓴다.</summary>
        public static Image Bar(Transform parent, float ratio, Color color, Color? back = null)
        {
            var go = Node("bar", parent);
            var background = go.AddComponent<Image>();
            background.color = back ?? Theme.Line;
            Stretch(go);

            var fillGo = Node("fill", go.transform);
            var fill = fillGo.AddComponent<Image>();
            fill.color = color;
            var rt = Rect(fillGo);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = new Vector2(Mathf.Clamp01(ratio), 1f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            return fill;
        }

        public static void Clear(Transform parent)
        {
            for (int i = parent.childCount - 1; i >= 0; i--)
                UnityEngine.Object.Destroy(parent.GetChild(i).gameObject);
        }
    }
}
