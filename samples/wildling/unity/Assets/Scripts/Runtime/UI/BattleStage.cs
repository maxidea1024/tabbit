using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Wildling.Game
{
    /// <summary>
    /// 전투에 선 개체 하나의 칸이다.
    /// </summary>
    /// <remarks>
    /// **칸은 움직이지 않고 안의 그림만 움직입니다.** 칸이 통째로 흔들리면 옆 칸과의 간격이
    /// 출렁여 줄 전체가 흐트러집니다. 차례가 온 것은 테두리로 알립니다.
    ///
    /// 연출은 계산과 분리되어 있습니다 — `Battle` 은 화면을 모르고 기록만 남기며, 이 칸이 그
    /// 기록의 한 박자를 받아 움직입니다. 배속을 바꿔도 결과가 달라지지 않는 이유입니다.
    /// </remarks>
    public sealed class BattleCell : MonoBehaviour
    {
        /// <summary>흔들리는 것. 칸이 아니라 안의 그림이다.</summary>
        public RectTransform Actor;

        public Image Portrait;
        public Image Overlay;           // 번쩍이는 덮개
        public Image Frame;             // 테두리. 차례가 오면 밝아진다
        public Image HpFill;

        /// <summary>
        /// 숫자와 배너가 그려지는 자리이다.
        /// </summary>
        /// <remarks>
        /// **칸 안이 아니라 화면 맨 위 층입니다.** 칸 안에 그리면 나중에 그려지는 옆 칸이
        /// 그 위를 덮고, 칸의 사각형에 잘립니다.
        /// </remarks>
        public RectTransform FloatRoot;

        /// <summary>적진이 어느 쪽인가. 어느 쪽으로 밀고 나갈지 정한다.</summary>
        public float Facing = 1f;

        /// <summary>쉬고 있을 때의 테두리 색. 등급의 색이다.</summary>
        public Color Rest = Color.white;

        private Vector2 _home;
        private Coroutine _move;
        private Coroutine _flash;
        private Coroutine _turn;

        private static readonly Color DamageColor = new(1f, 0.36f, 0.30f);
        private static readonly Color CritColor = new(1f, 0.80f, 0.24f);
        private static readonly Color HealColor = new(0.40f, 0.92f, 0.54f);
        private static readonly Color BuffColor = new(0.52f, 0.76f, 1f);
        private static readonly Color MissColor = new(0.78f, 0.80f, 0.88f);
        private static readonly Color StrongColor = new(1f, 0.60f, 0.20f);
        private static readonly Color WeakColor = new(0.60f, 0.70f, 0.88f);
        private static readonly Color TurnColor = new(1f, 0.92f, 0.45f);

        private Vector2 Anchor
        {
            get
            {
                if (FloatRoot == null || Actor == null)
                    return Vector2.zero;
                return FloatRoot.InverseTransformPoint(Actor.position);
            }
        }

        private void Awake() => _home = Actor != null ? Actor.anchoredPosition : Vector2.zero;

        // ------------------------------------------------------------ 박자

        public void Play(BeatKind kind, int amount, bool crit, bool isTarget, int affinity = 0)
        {
            switch (kind)
            {
                case BeatKind.Damage:
                {
                    Shake(crit ? 14f : 8f, crit ? 0.32f : 0.20f);
                    Blink(crit ? CritColor : DamageColor, crit ? 0.60f : 0.42f);

                    string mark = affinity > Numbers.One ? "▲"
                        : affinity > 0 && affinity < Numbers.One ? "▼" : "";
                    var tint = affinity > Numbers.One ? StrongColor
                        : affinity > 0 && affinity < Numbers.One ? WeakColor
                        : crit ? CritColor : DamageColor;

                    Float(amount + mark, tint, crit ? 42 : 32, crit);
                    Burst(tint, crit ? 10 : 5, crit ? 150f : 96f);
                    break;
                }

                case BeatKind.Heal:
                    Blink(HealColor, 0.34f);
                    Float("+" + amount, HealColor, 30, false);
                    Burst(HealColor, 6, 100f);
                    break;

                case BeatKind.Buff:
                    Blink(BuffColor, 0.28f);
                    Float(amount >= 0 ? "▲" : "▼", BuffColor, 28, false);
                    Burst(BuffColor, 5, 88f);
                    break;

                case BeatKind.Status:
                    Shake(5f, 0.16f);
                    Blink(BuffColor, 0.26f);
                    Burst(BuffColor, 5, 80f);
                    break;

                case BeatKind.Miss:
                    Float("빗맞음", MissColor, 24, false);
                    break;

                case BeatKind.Down:
                    Blink(DamageColor, 0.7f);
                    Burst(DamageColor, 12, 170f);
                    break;

                case BeatKind.Act when !isTarget:
                    Nudge();
                    break;

                case BeatKind.Line:
                    break;
            }
        }

        /// <summary>쓰러졌으면 어둡게 둔다.</summary>
        public void SetDown(bool down)
        {
            if (Portrait != null)
                Portrait.color = down ? new Color(0.30f, 0.30f, 0.36f, 0.80f) : Color.white;
        }

        /// <summary>
        /// 지금 이 칸의 차례인가.
        /// </summary>
        /// <remarks>
        /// **차례는 테두리로 알립니다.** 칸을 움찔거리게 하면 옆 칸과의 간격이 출렁여 줄
        /// 전체가 흔들립니다. 움직이는 것은 실제로 무언가 할 때의 그림 하나뿐입니다.
        /// </remarks>
        public void SetTurn(bool active)
        {
            if (Frame == null)
                return;

            if (_turn != null)
            {
                StopCoroutine(_turn);
                _turn = null;
            }

            if (!active)
            {
                Frame.color = Rest;
                return;
            }

            _turn = StartCoroutine(TurnRoutine());
        }

        private IEnumerator TurnRoutine()
        {
            float time = 0f;
            while (Frame != null)
            {
                time += Time.deltaTime;
                float pulse = 0.70f + Mathf.Sin(time * 8f) * 0.30f;
                Frame.color = new Color(TurnColor.r, TurnColor.g, TurnColor.b, pulse);
                yield return null;
            }
        }

        // ------------------------------------------------------------ 움직임

        /// <summary>안의 그림이 적진 쪽으로 밀고 나갔다 돌아온다.</summary>
        public void Nudge()
        {
            if (Actor == null)
                return;
            if (_move != null)
                StopCoroutine(_move);
            _move = StartCoroutine(NudgeRoutine());
        }

        private IEnumerator NudgeRoutine()
        {
            float time = 0f;
            const float span = 0.30f;
            while (time < span)
            {
                time += Time.deltaTime;
                float t = Mathf.Clamp01(time / span);
                float push = t < 0.35f ? t / 0.35f : 1f - (t - 0.35f) / 0.65f;
                Actor.anchoredPosition = _home + new Vector2(0f, push * 24f * Facing);
                Actor.localScale = Vector3.one * (1f + push * 0.08f);
                yield return null;
            }
            Actor.anchoredPosition = _home;
            Actor.localScale = Vector3.one;
            _move = null;
        }

        public void Shake(float strength, float span)
        {
            if (Actor == null)
                return;
            if (_move != null)
                StopCoroutine(_move);
            _move = StartCoroutine(ShakeRoutine(strength, span));
        }

        private IEnumerator ShakeRoutine(float strength, float span)
        {
            float time = 0f;
            while (time < span)
            {
                time += Time.deltaTime;
                float fade = 1f - Mathf.Clamp01(time / span);
                float wave = Mathf.Sin(time * 62f) * strength * fade;
                Actor.anchoredPosition = _home + new Vector2(wave, wave * 0.35f);
                yield return null;
            }
            Actor.anchoredPosition = _home;
            _move = null;
        }

        public void Blink(Color color, float peak)
        {
            if (Overlay == null)
                return;
            if (_flash != null)
                StopCoroutine(_flash);
            _flash = StartCoroutine(BlinkRoutine(color, peak));
        }

        private IEnumerator BlinkRoutine(Color color, float peak)
        {
            float time = 0f;
            const float span = 0.30f;
            while (time < span)
            {
                time += Time.deltaTime;
                float t = Mathf.Clamp01(time / span);
                Overlay.color = new Color(color.r, color.g, color.b, (1f - t) * peak);
                yield return null;
            }
            Overlay.color = new Color(color.r, color.g, color.b, 0f);
            _flash = null;
        }

        /// <summary>이 칸에서 별이 튄다.</summary>
        public void Burst(Color color, int count, float reach)
            => Fx.Sparks(FloatRoot, Anchor, color, count, reach);

        /// <summary>
        /// 숫자가 떠올랐다 사라진다.
        /// </summary>
        /// <remarks>
        /// 같은 자리에 겹치지 않도록 시작 지점을 조금씩 흩습니다 — 광역기는 세 칸에 동시에
        /// 뜨고, 효과가 둘인 스킬은 한 칸에 두 번 뜹니다.
        /// </remarks>
        public void Float(string text, Color color, int size, bool crit)
        {
            if (FloatRoot == null || string.IsNullOrEmpty(text))
                return;

            var go = Ui.Node("float", FloatRoot);
            var rt = Ui.Rect(go);
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(180f, 44f);
            rt.anchoredPosition = Anchor
                                  + new Vector2(Random.Range(-24f, 24f), Random.Range(-8f, 12f));

            var label = go.AddComponent<Text>();
            label.font = Theme.Font;
            label.fontSize = size;
            label.fontStyle = FontStyle.Bold;
            label.text = text;
            label.color = color;
            label.alignment = TextAnchor.MiddleCenter;
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            label.raycastTarget = false;

            var outline = go.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.9f);
            outline.effectDistance = new Vector2(2.5f, -2.5f);

            StartCoroutine(FloatRoutine(rt, label, crit));
        }

        private static IEnumerator FloatRoutine(RectTransform rt, Graphic label, bool crit)
        {
            Vector2 from = rt.anchoredPosition;
            float span = crit ? 0.95f : 0.75f;
            float rise = crit ? 100f : 76f;
            float time = 0f;

            while (time < span && rt != null)
            {
                time += Time.deltaTime;
                float t = Mathf.Clamp01(time / span);

                rt.anchoredPosition = from + new Vector2(0f, Mathf.Sqrt(t) * rise);

                float pop = crit ? 1f + Mathf.Sin(Mathf.Clamp01(t * 4f) * Mathf.PI) * 0.35f : 1f;
                rt.localScale = Vector3.one * pop;

                var c = label.color;
                label.color = new Color(c.r, c.g, c.b, t < 0.62f ? 1f : (1f - t) / 0.38f);
                yield return null;
            }

            if (rt != null)
                Destroy(rt.gameObject);
        }

        /// <summary>
        /// 무엇을 했는지 칸 위에 적는다.
        /// </summary>
        /// <remarks>
        /// 스킬 이름과 아이콘입니다. **글이 흐르는 목록만으로는 누가 무엇을 했는지 읽어야
        /// 알 수 있습니다** — 쓴 자리에 그것을 띄우면 보고 알 수 있습니다.
        /// </remarks>
        public void Banner(string text, Sprite icon)
        {
            if (FloatRoot == null || string.IsNullOrEmpty(text))
                return;

            var go = Ui.Node("banner", FloatRoot);
            var rt = Ui.Rect(go);
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(190f, 40f);
            rt.anchoredPosition = Anchor + new Vector2(0f, 78f);

            var plate = go.AddComponent<Image>();
            plate.sprite = Skin.PanelSunk;
            plate.type = Image.Type.Sliced;
            plate.color = new Color(0.07f, 0.08f, 0.12f, 0.92f);
            plate.raycastTarget = false;

            if (icon != null)
            {
                var slot = Ui.Node("icon", go.transform);
                var srt = Ui.Rect(slot);
                srt.anchorMin = new Vector2(0f, 0.5f);
                srt.anchorMax = new Vector2(0f, 0.5f);
                srt.pivot = new Vector2(0f, 0.5f);
                srt.sizeDelta = new Vector2(30f, 30f);
                srt.anchoredPosition = new Vector2(6f, 0f);
                Ui.Icon(slot.transform, icon);
            }

            var body = Ui.Node("text", go.transform);
            var trt = Ui.Stretch(body);
            trt.offsetMin = new Vector2(icon != null ? 42f : 10f, 0f);
            trt.offsetMax = new Vector2(-10f, 0f);
            var label = Ui.Label(body.transform, text, 20, Theme.OnDark, TextAnchor.MiddleLeft);
            label.horizontalOverflow = HorizontalWrapMode.Overflow;

            StartCoroutine(BannerRoutine(rt, go));
        }

        private static IEnumerator BannerRoutine(RectTransform rt, GameObject go)
        {
            var graphics = go.GetComponentsInChildren<Graphic>();
            var from = rt.anchoredPosition;
            float time = 0f;
            const float span = 0.85f;

            while (time < span && rt != null)
            {
                time += Time.deltaTime;
                float t = Mathf.Clamp01(time / span);
                rt.anchoredPosition = from + new Vector2(0f, t * 12f);
                rt.localScale = Vector3.one * (t < 0.12f ? Mathf.Lerp(0.7f, 1f, t / 0.12f) : 1f);

                float alpha = t < 0.68f ? 1f : (1f - t) / 0.32f;
                foreach (var g in graphics)
                {
                    if (g == null)
                        continue;
                    var c = g.color;
                    g.color = new Color(c.r, c.g, c.b, alpha * (g is Image ? 0.92f : 1f));
                }
                yield return null;
            }

            if (go != null)
                Destroy(go);
        }
    }

    /// <summary>전투 화면의 한 진영이다.</summary>
    public static class BattleStage
    {
        /// <summary>
        /// 진영 하나를 세우고 칸들을 낸다.
        /// </summary>
        /// <remarks>
        /// 칸 하나는 **속성 색 바탕 · 그림 · 등급 테두리** 세 겹입니다. 그림은 칸을 꽉 채우고,
        /// 이름과 체력은 그 아래에 놓입니다.
        /// </remarks>
        public static List<BattleCell> Build(Transform parent, List<Combatant> side,
                                             float facing, RectTransform floatLayer)
        {
            var cells = new List<BattleCell>();
            var row = Ui.Row(parent, 10f, 8f);

            foreach (var c in side)
            {
                var slot = Ui.Node(c.Monster.MonsterId, row);
                var cell = slot.AddComponent<BattleCell>();

                // 그림이 들어갈 정사각 자리. 칸의 위쪽을 차지합니다.
                var box = Ui.Node("box", slot.transform);
                var brt = Ui.Rect(box);
                brt.anchorMin = new Vector2(0.5f, 1f);
                brt.anchorMax = new Vector2(0.5f, 1f);
                brt.pivot = new Vector2(0.5f, 1f);
                brt.sizeDelta = new Vector2(130f, 130f);
                brt.anchoredPosition = new Vector2(0f, -2f);

                var bed = box.AddComponent<Image>();
                bed.sprite = Skin.PanelSunk;
                bed.type = Image.Type.Sliced;
                bed.color = Theme.Element(c.Monster.Element) * 0.6f;
                bed.raycastTarget = false;

                // **흔들리는 것은 이 안입니다.** 칸 자체는 제자리에 있습니다.
                var actor = Ui.Node("actor", box.transform);
                cell.Actor = Ui.Stretch(actor, 5f);

                cell.Portrait = Ui.Icon(actor.transform, ArtLibrary.Icon(c.Monster.Icon));
                cell.Portrait.preserveAspect = false;   // 칸을 꽉 채웁니다

                var flash = Ui.Node("flash", actor.transform);
                Ui.Stretch(flash);
                cell.Overlay = flash.AddComponent<Image>();
                cell.Overlay.sprite = cell.Portrait.sprite;
                cell.Overlay.preserveAspect = false;
                cell.Overlay.raycastTarget = false;
                cell.Overlay.color = new Color(1f, 1f, 1f, 0f);

                // 등급 테두리. 차례가 오면 여기가 밝아집니다.
                var frame = Ui.Node("frame", box.transform);
                Ui.Stretch(frame);
                cell.Frame = frame.AddComponent<Image>();
                cell.Frame.sprite = Skin.Frame(c.Monster.Grade);
                cell.Frame.type = Image.Type.Sliced;
                cell.Frame.raycastTarget = false;
                cell.Rest = Theme.Grade(c.Monster.Grade);
                cell.Frame.color = cell.Rest;

                // 속성 배지.
                var badge = Ui.Node("element", box.transform);
                var art = Ui.Rect(badge);
                art.anchorMin = new Vector2(0f, 1f);
                art.anchorMax = new Vector2(0f, 1f);
                art.pivot = new Vector2(0f, 1f);
                art.sizeDelta = new Vector2(50f, 24f);
                art.anchoredPosition = new Vector2(5f, -5f);
                var plate = badge.AddComponent<Image>();
                plate.sprite = Skin.PanelSunk;
                plate.type = Image.Type.Sliced;
                plate.color = Theme.Element(c.Monster.Element);
                plate.raycastTarget = false;
                Ui.Label(badge.transform, Theme.Label(c.Monster.Element), 15,
                         Theme.OnColor, TextAnchor.MiddleCenter);

                // 이름과 체력은 칸 아래입니다.
                var name = Ui.Node("name", slot.transform);
                var nrt = Ui.Rect(name);
                nrt.anchorMin = new Vector2(0f, 0f);
                nrt.anchorMax = new Vector2(1f, 0f);
                nrt.pivot = new Vector2(0.5f, 0f);
                nrt.offsetMin = new Vector2(2f, 26f);
                nrt.offsetMax = new Vector2(-2f, 50f);
                Ui.Label(name.transform, $"{c.Name} Lv{c.Level}", 17, Theme.OnDark,
                         TextAnchor.MiddleCenter);

                var bar = Ui.Node("hp", slot.transform);
                var hrt = Ui.Rect(bar);
                hrt.anchorMin = new Vector2(0f, 0f);
                hrt.anchorMax = new Vector2(1f, 0f);
                hrt.pivot = new Vector2(0.5f, 0f);
                hrt.offsetMin = new Vector2(4f, 4f);
                hrt.offsetMax = new Vector2(-4f, 20f);
                cell.HpFill = Ui.Bar(bar.transform, 1f, Theme.Good);

                cell.FloatRoot = floatLayer;
                cell.Facing = facing;
                cells.Add(cell);
            }

            return cells;
        }
    }
}
