using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Wildling.Game
{
    /// <summary>
    /// 전투에 선 개체 하나의 칸이다. 흔들리고 번쩍이고 숫자를 띄운다.
    /// </summary>
    /// <remarks>
    /// **연출은 계산과 분리되어 있습니다.** `Battle` 은 화면을 모르고 기록만 남기며, 이 칸은
    /// 그 기록의 한 박자를 받아 움직입니다 — 배속을 바꿔도 결과가 달라지지 않는 이유입니다.
    /// </remarks>
    public sealed class BattleCell : MonoBehaviour
    {
        public RectTransform Body;      // 흔들리는 것
        public Image Portrait;
        public Image Overlay;           // 번쩍이는 덮개
        public Image HpFill;

        /// <summary>
        /// 숫자와 배너가 그려지는 자리이다.
        /// </summary>
        /// <remarks>
        /// **칸 안이 아니라 화면 맨 위 층입니다.** 칸 안에 그리면 나중에 그려지는 옆 칸이
        /// 그 위를 덮고, 칸의 사각형에 잘립니다.
        /// </remarks>
        public RectTransform FloatRoot;

        private Vector2 _home;
        private Coroutine _shake;
        private Coroutine _flash;

        private static readonly Color DamageColor = new(1f, 0.42f, 0.34f);
        private static readonly Color CritColor = new(1f, 0.82f, 0.30f);
        private static readonly Color HealColor = new(0.52f, 0.92f, 0.60f);
        private static readonly Color BuffColor = new(0.62f, 0.80f, 1f);
        private static readonly Color MissColor = new(0.72f, 0.74f, 0.82f);
        private static readonly Color StrongColor = new(1f, 0.62f, 0.24f);
        private static readonly Color WeakColor = new(0.62f, 0.70f, 0.86f);

        /// <summary>적진이 어느 쪽인가. 위로 돌진할지 아래로 돌진할지 정한다.</summary>
        public float Facing = 1f;

        /// <summary>이 칸의 가운데가 뜨는 층에서 어디인가.</summary>
        private Vector2 Anchor
        {
            get
            {
                if (FloatRoot == null || Body == null)
                    return Vector2.zero;
                return FloatRoot.InverseTransformPoint(Body.position);
            }
        }

        private void Awake() => _home = Body != null ? Body.anchoredPosition : Vector2.zero;

        // ------------------------------------------------------------ 박자

        public void Play(BeatKind kind, int amount, bool crit, bool isTarget,
                         int affinity = 0)
        {
            switch (kind)
            {
                case BeatKind.Damage:
                {
                    Shake(crit ? 16f : 9f, crit ? 0.34f : 0.22f);
                    Blink(crit ? CritColor : DamageColor, crit ? 0.55f : 0.38f);

                    // **상성이 숫자 옆에 붙습니다.** 왜 크게 들어갔는지 로그를 읽지 않아도
                    // 보이는 자리입니다.
                    string mark = affinity > Numbers.One ? "▲"
                                : affinity > 0 && affinity < Numbers.One ? "▼" : "";
                    var tint = affinity > Numbers.One ? StrongColor
                             : affinity > 0 && affinity < Numbers.One ? WeakColor
                             : crit ? CritColor : DamageColor;

                    Float(amount + mark, tint, crit ? 40 : 30, crit);
                    break;
                }

                case BeatKind.Heal:
                    Blink(HealColor, 0.30f);
                    Float("+" + amount, HealColor, 28, false);
                    break;

                case BeatKind.Buff:
                    Blink(BuffColor, 0.24f);
                    Float(amount >= 0 ? "▲" : "▼", BuffColor, 26, false);
                    break;

                case BeatKind.Status:
                    Shake(5f, 0.16f);
                    Blink(BuffColor, 0.22f);
                    break;

                case BeatKind.Miss:
                    Float("빗맞음", MissColor, 22, false);
                    break;

                case BeatKind.Line:
                    break;

                case BeatKind.Down:
                    Blink(DamageColor, 0.6f);
                    break;

                case BeatKind.Act when !isTarget:
                    Nudge();
                    break;
            }
        }

        /// <summary>쓰러졌으면 어둡게 둔다.</summary>
        public void SetDown(bool down)
        {
            if (Portrait != null)
                Portrait.color = down ? new Color(0.34f, 0.34f, 0.40f, 0.75f) : Color.white;
        }

        // ------------------------------------------------------------ 움직임

        /// <summary>움직인 쪽이 적진 쪽으로 밀고 나갔다 돌아온다.</summary>
        public void Nudge()
        {
            if (Body == null)
                return;
            if (_shake != null)
                StopCoroutine(_shake);
            _shake = StartCoroutine(NudgeRoutine());
        }

        private IEnumerator NudgeRoutine()
        {
            float time = 0f;
            const float span = 0.30f;
            while (time < span)
            {
                time += Time.deltaTime;
                float t = Mathf.Clamp01(time / span);
                // 빠르게 나갔다 천천히 돌아옵니다.
                float push = t < 0.35f ? t / 0.35f : 1f - (t - 0.35f) / 0.65f;
                Body.anchoredPosition = _home + new Vector2(0f, push * 26f * Facing);
                Body.localScale = Vector3.one * (1f + push * 0.06f);
                yield return null;
            }
            Body.anchoredPosition = _home;
            Body.localScale = Vector3.one;
            _shake = null;
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
            rt.sizeDelta = new Vector2(184f, 38f);
            // **언제나 위쪽입니다.** 바라보는 쪽으로 띄우면 두 진영 사이에서 겹칩니다.
            rt.anchoredPosition = Anchor + new Vector2(0f, 66f);

            var plate = go.AddComponent<Image>();
            plate.color = new Color(0.06f, 0.07f, 0.10f, 0.88f);
            plate.raycastTarget = false;

            if (icon != null)
            {
                var slot = Ui.Node("icon", go.transform);
                var srt = Ui.Rect(slot);
                srt.anchorMin = new Vector2(0f, 0.5f);
                srt.anchorMax = new Vector2(0f, 0.5f);
                srt.pivot = new Vector2(0f, 0.5f);
                srt.sizeDelta = new Vector2(32f, 32f);
                srt.anchoredPosition = new Vector2(5f, 0f);
                Ui.Icon(slot.transform, icon);
            }

            var text_ = Ui.Node("text", go.transform);
            var trt = Ui.Stretch(text_);
            trt.offsetMin = new Vector2(icon != null ? 42f : 8f, 0f);
            trt.offsetMax = new Vector2(-8f, 0f);
            var label = Ui.Label(text_.transform, text, 20, Theme.Text, TextAnchor.MiddleLeft);
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
                rt.anchoredPosition = from + new Vector2(0f, t * 10f);
                rt.localScale = Vector3.one * (t < 0.12f ? Mathf.Lerp(0.7f, 1f, t / 0.12f) : 1f);

                float alpha = t < 0.68f ? 1f : (1f - t) / 0.32f;
                foreach (var g in graphics)
                {
                    if (g == null)
                        continue;
                    var c = g.color;
                    g.color = new Color(c.r, c.g, c.b, alpha * (g is Image ? 0.88f : 1f));
                }
                yield return null;
            }

            if (go != null)
                Destroy(go);
        }

        public void Shake(float strength, float span)
        {
            if (Body == null)
                return;
            if (_shake != null)
                StopCoroutine(_shake);
            _shake = StartCoroutine(ShakeRoutine(strength, span));
        }

        private IEnumerator ShakeRoutine(float strength, float span)
        {
            float time = 0f;
            while (time < span)
            {
                time += Time.deltaTime;
                float fade = 1f - Mathf.Clamp01(time / span);
                float wave = Mathf.Sin(time * 62f) * strength * fade;
                Body.anchoredPosition = _home + new Vector2(wave, wave * 0.35f);
                yield return null;
            }
            Body.anchoredPosition = _home;
            _shake = null;
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
            label.fontStyle = crit ? FontStyle.Bold : FontStyle.Normal;
            label.text = text;
            label.color = color;
            label.alignment = TextAnchor.MiddleCenter;
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            label.raycastTarget = false;

            var outline = go.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.85f);
            outline.effectDistance = new Vector2(2f, -2f);

            StartCoroutine(FloatRoutine(rt, label, crit));
        }

        private static IEnumerator FloatRoutine(RectTransform rt, Graphic label, bool crit)
        {
            Vector2 from = rt.anchoredPosition;
            float span = crit ? 0.95f : 0.75f;
            float rise = crit ? 96f : 72f;
            float time = 0f;

            while (time < span && rt != null)
            {
                time += Time.deltaTime;
                float t = Mathf.Clamp01(time / span);

                rt.anchoredPosition = from + new Vector2(0f, Mathf.Sqrt(t) * rise);

                // 치명타는 잠깐 커졌다 제자리로 돌아옵니다.
                float pop = crit ? 1f + Mathf.Sin(Mathf.Clamp01(t * 4f) * Mathf.PI) * 0.35f : 1f;
                rt.localScale = Vector3.one * pop;

                var c = label.color;
                label.color = new Color(c.r, c.g, c.b, t < 0.62f ? 1f : (1f - t) / 0.38f);
                yield return null;
            }

            if (rt != null)
                Destroy(rt.gameObject);
        }
    }

    /// <summary>전투 화면의 한 진영이다.</summary>
    public static class BattleStage
    {
        /// <summary>진영 하나를 세우고 칸들을 낸다.</summary>
        public static List<BattleCell> Build(Transform parent, List<Combatant> side,
                                             float facing, RectTransform floatLayer)
        {
            var cells = new List<BattleCell>();
            var row = Ui.Row(parent, 8f, 8f);

            foreach (var c in side)
            {
                var slot = Ui.Node(c.Monster.MonsterId, row);

                // 흔들리는 것은 안쪽입니다 — 바깥은 층이 자리를 정하므로 건드리지 않습니다.
                var body = Ui.Node("body", slot.transform);
                Ui.Stretch(body);
                var cell = body.AddComponent<BattleCell>();
                cell.Body = Ui.Rect(body);

                Ui.Panel(body.transform, new Color(0f, 0f, 0f, 0.35f));

                var icon = Ui.Node("icon", body.transform);
                var irt = Ui.Rect(icon);
                irt.anchorMin = new Vector2(0.5f, 1f);
                irt.anchorMax = new Vector2(0.5f, 1f);
                irt.pivot = new Vector2(0.5f, 1f);
                irt.sizeDelta = new Vector2(104f, 104f);
                irt.anchoredPosition = new Vector2(0f, -6f);
                cell.Portrait = Ui.Icon(icon.transform, ArtLibrary.Icon(c.Monster.Icon));

                var flash = Ui.Node("flash", icon.transform);
                Ui.Stretch(flash);
                cell.Overlay = flash.AddComponent<Image>();
                cell.Overlay.sprite = cell.Portrait.sprite;
                cell.Overlay.preserveAspect = true;
                cell.Overlay.raycastTarget = false;
                cell.Overlay.color = new Color(1f, 1f, 1f, 0f);

                var name = Ui.Node("name", body.transform);
                var nrt = Ui.Rect(name);
                nrt.anchorMin = new Vector2(0f, 0f);
                nrt.anchorMax = new Vector2(1f, 0f);
                nrt.pivot = new Vector2(0.5f, 0f);
                nrt.offsetMin = new Vector2(4f, 30f);
                nrt.offsetMax = new Vector2(-4f, 54f);
                Ui.Label(name.transform, $"{c.Name} Lv{c.Level}", 17, Theme.Text,
                         TextAnchor.MiddleCenter);

                var bar = Ui.Node("hp", body.transform);
                var brt = Ui.Rect(bar);
                brt.anchorMin = new Vector2(0f, 0f);
                brt.anchorMax = new Vector2(1f, 0f);
                brt.pivot = new Vector2(0.5f, 0f);
                brt.offsetMin = new Vector2(6f, 8f);
                brt.offsetMax = new Vector2(-6f, 22f);
                cell.HpFill = Ui.Bar(bar.transform, 1f, Theme.Good);

                // **속성 배지.** 편성이 판단이 되려면 무엇과 싸우는지가 보여야 합니다.
                var badge = Ui.Node("element", body.transform);
                var brt2 = Ui.Rect(badge);
                brt2.anchorMin = new Vector2(0f, 1f);
                brt2.anchorMax = new Vector2(0f, 1f);
                brt2.pivot = new Vector2(0f, 1f);
                brt2.sizeDelta = new Vector2(52f, 24f);
                brt2.anchoredPosition = new Vector2(6f, -6f);
                var plate = badge.AddComponent<Image>();
                plate.color = Theme.Element(c.Monster.Element);
                plate.raycastTarget = false;
                Ui.Label(badge.transform, Theme.Label(c.Monster.Element), 16,
                         new Color(0.07f, 0.07f, 0.10f), TextAnchor.MiddleCenter);

                cell.FloatRoot = floatLayer;
                cell.Facing = facing;

                cells.Add(cell);
            }

            return cells;
        }
    }
}
