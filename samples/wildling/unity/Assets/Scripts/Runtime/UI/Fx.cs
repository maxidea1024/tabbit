using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace Wildling.Game
{
    /// <summary>
    /// 훑고 지나가는 빛이다.
    /// </summary>
    /// <remarks>
    /// 「지금 눌러야 하는 것」에만 붙입니다. 전부에 붙이면 아무것도 눈에 띄지 않습니다.
    /// </remarks>
    public sealed class Shine : MonoBehaviour
    {
        public float Period = 2.4f;
        public float Span = 0.55f;

        private RectTransform _strip;

        public static void Attach(GameObject target, float period = 2.4f)
        {
            if (target == null || Skin.Shine is null)
                return;

            var host = Ui.Node("shine", target.transform);
            Ui.Stretch(host);
            host.AddComponent<RectMask2D>();

            var strip = Ui.Node("strip", host.transform);
            var image = strip.AddComponent<Image>();
            image.sprite = Skin.Shine;
            image.color = new Color(1f, 1f, 1f, 0.55f);
            image.raycastTarget = false;

            var rt = Ui.Rect(strip);
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(90f, 0f);
            rt.localRotation = Quaternion.Euler(0f, 0f, 14f);

            var shine = host.AddComponent<Shine>();
            shine.Period = period;
            shine._strip = rt;
        }

        private IEnumerator Start()
        {
            var host = (RectTransform)transform;
            while (true)
            {
                float width = host.rect.width + 140f;
                float time = 0f;
                while (time < Span)
                {
                    time += Time.deltaTime;
                    float t = Mathf.Clamp01(time / Span);
                    _strip.anchoredPosition = new Vector2(-70f + width * t, 0f);
                    yield return null;
                }
                _strip.anchoredPosition = new Vector2(-9999f, 0f);
                yield return new WaitForSeconds(Period - Span);
            }
        }
    }

    /// <summary>
    /// 터지는 것들이다 — 빛무리 · 별 · 튀어 오르는 글자.
    /// </summary>
    /// <remarks>
    /// **받은 순간이 보여야 다시 하고 싶어집니다.** 값이 조용히 바뀌면 무슨 일이 있었는지
    /// 읽어야 알게 되고, 그러면 한 바퀴를 더 돌 이유가 약해집니다.
    /// </remarks>
    public static class Fx
    {
        /// <summary>가운데에서 빛이 한 번 퍼진다.</summary>
        public static void Burst(Transform layer, Vector2 at, Color color, float size = 240f)
        {
            if (layer == null || Skin.Glow is null)
                return;

            var go = Ui.Node("burst", layer);
            var rt = Center(go, at, size);
            var image = go.AddComponent<Image>();
            image.sprite = Skin.Glow;
            image.color = color;
            image.raycastTarget = false;

            Run(layer, Fade(rt, image, 0.55f, 0.35f, 1.5f));
        }

        /// <summary>별이 사방으로 튄다.</summary>
        public static void Sparks(Transform layer, Vector2 at, Color color, int count = 7,
                                  float reach = 130f)
        {
            if (layer == null || Skin.Spark is null)
                return;

            for (int i = 0; i < count; i++)
            {
                var go = Ui.Node("spark", layer);
                var rt = Center(go, at, Random.Range(26f, 46f));
                var image = go.AddComponent<Image>();
                image.sprite = Skin.Spark;
                image.color = color;
                image.raycastTarget = false;

                float angle = (i / (float)count) * Mathf.PI * 2f + Random.Range(-0.3f, 0.3f);
                var to = at + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle))
                              * reach * Random.Range(0.6f, 1.25f);
                Run(layer, Fly(rt, image, at, to, Random.Range(0.45f, 0.8f)));
            }
        }

        /// <summary>글자가 커졌다 사라진다. 레벨 상승·각성처럼 큰 사건에 쓴다.</summary>
        public static void Shout(Transform layer, string text, Color color, int size = 56)
        {
            if (layer == null || string.IsNullOrEmpty(text))
                return;

            var go = Ui.Node("shout", layer);
            var rt = Ui.Rect(go);
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(600f, 100f);
            rt.anchoredPosition = new Vector2(0f, 90f);

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
            outline.effectDistance = new Vector2(3f, -3f);

            Burst(layer, rt.anchoredPosition, new Color(color.r, color.g, color.b, 0.5f), 420f);
            Sparks(layer, rt.anchoredPosition, color, 10, 180f);
            Run(layer, Pop(rt, label));
        }

        /// <summary>화면이 한 번 하얘진다.</summary>
        public static void Flash(Transform layer, Color color, float peak = 0.30f)
        {
            if (layer == null)
                return;

            var go = Ui.Node("flash", layer);
            Ui.Stretch(go);
            var image = go.AddComponent<Image>();
            image.color = new Color(color.r, color.g, color.b, peak);
            image.raycastTarget = false;
            Run(layer, Fade(Ui.Rect(go), image, peak, 0.22f, 1f));
        }

        // ------------------------------------------------------------ 속

        private static RectTransform Center(GameObject go, Vector2 at, float size)
        {
            var rt = Ui.Rect(go);
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(size, size);
            rt.anchoredPosition = at;
            return rt;
        }

        private static void Run(Transform layer, IEnumerator routine)
        {
            var host = layer.GetComponentInParent<MonoBehaviour>();
            if (host == null)
                host = App.Current;
            if (host != null)
                host.StartCoroutine(routine);
        }

        private static IEnumerator Fade(RectTransform rt, Graphic graphic, float peak,
                                        float span, float grow)
        {
            float time = 0f;
            Vector2 from = rt.sizeDelta;
            while (time < span && rt != null)
            {
                time += Time.deltaTime;
                float t = Mathf.Clamp01(time / span);
                rt.sizeDelta = from * Mathf.Lerp(1f, grow, t);
                var c = graphic.color;
                graphic.color = new Color(c.r, c.g, c.b, peak * (1f - t));
                yield return null;
            }
            if (rt != null)
                Object.Destroy(rt.gameObject);
        }

        private static IEnumerator Fly(RectTransform rt, Graphic graphic, Vector2 from,
                                       Vector2 to, float span)
        {
            float time = 0f;
            while (time < span && rt != null)
            {
                time += Time.deltaTime;
                float t = Mathf.Clamp01(time / span);
                rt.anchoredPosition = Vector2.Lerp(from, to, Mathf.Sqrt(t));
                rt.localRotation = Quaternion.Euler(0f, 0f, t * 180f);
                rt.localScale = Vector3.one * (1f - t * 0.7f);
                var c = graphic.color;
                graphic.color = new Color(c.r, c.g, c.b, 1f - t);
                yield return null;
            }
            if (rt != null)
                Object.Destroy(rt.gameObject);
        }

        private static IEnumerator Pop(RectTransform rt, Graphic graphic)
        {
            float time = 0f;
            const float span = 1.15f;
            while (time < span && rt != null)
            {
                time += Time.deltaTime;
                float t = Mathf.Clamp01(time / span);

                float scale = t < 0.18f
                    ? Mathf.Lerp(0.4f, 1.18f, t / 0.18f)
                    : t < 0.32f ? Mathf.Lerp(1.18f, 1f, (t - 0.18f) / 0.14f) : 1f;
                rt.localScale = Vector3.one * scale;
                rt.anchoredPosition = new Vector2(0f, 90f + t * 40f);

                var c = graphic.color;
                graphic.color = new Color(c.r, c.g, c.b, t < 0.72f ? 1f : (1f - t) / 0.28f);
                yield return null;
            }
            if (rt != null)
                Object.Destroy(rt.gameObject);
        }
    }
}
