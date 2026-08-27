using UnityEngine;
using UnityEngine.UI;

namespace Wildling.Game
{
    /// <summary>
    /// 배경이 옆으로 끝없이 흘러간다.
    /// </summary>
    /// <remarks>
    /// **같은 그림 둘을 나란히 놓고 밀어 냅니다.** 하나가 왼쪽으로 완전히 빠지면 오른쪽 끝으로
    /// 돌려보내므로, 그림 한 장으로 끝없이 이어집니다 — 배경의 좌우가 맞물리게 그려져 있어
    /// 이음매가 보이지 않습니다.
    ///
    /// 방치형에서 화면이 멈춰 있으면 진행도 멈춘 것처럼 보입니다. 흐르는 배경이 그 인상을
    /// 지웁니다.
    /// </remarks>
    public sealed class Parallax : MonoBehaviour
    {
        public float Speed = 22f;

        private RectTransform _a;
        private RectTransform _b;
        private float _width;

        /// <summary>
        /// 그 자리에 흐르는 배경을 깐다.
        /// </summary>
        /// <param name="host">채울 자리. 이 사각형 밖으로는 나가지 않는다.</param>
        public static void Attach(GameObject host, Sprite sprite, float speed = 22f,
                                  Color? tint = null)
        {
            if (host == null || sprite is null)
                return;

            var view = Ui.Node("parallax", host.transform);
            Ui.Stretch(view);
            view.AddComponent<RectMask2D>();

            var parallax = view.AddComponent<Parallax>();
            parallax.Speed = speed;
            parallax._a = Piece(view.transform, sprite, tint);
            parallax._b = Piece(view.transform, sprite, tint);
        }

        private static RectTransform Piece(Transform parent, Sprite sprite, Color? tint)
        {
            var go = Ui.Node("piece", parent);
            var image = go.AddComponent<Image>();
            image.sprite = sprite;
            image.preserveAspect = false;
            image.color = tint ?? Color.white;
            image.raycastTarget = false;

            var rt = Ui.Rect(go);
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 0.5f);
            rt.offsetMin = new Vector2(0f, 0f);
            rt.offsetMax = new Vector2(0f, 0f);
            return rt;
        }

        private void LateUpdate()
        {
            if (_a == null || _b == null)
                return;

            var host = (RectTransform)transform;
            float height = host.rect.height;
            if (height <= 0f)
                return;

            // 그림의 가로세로 비를 지키면서 자리를 세로로 꽉 채웁니다.
            var sprite = _a.GetComponent<Image>().sprite;
            float wanted = height * sprite.rect.width / sprite.rect.height;
            if (wanted < host.rect.width)
                wanted = host.rect.width;

            if (!Mathf.Approximately(wanted, _width))
            {
                _width = wanted;
                _a.sizeDelta = new Vector2(_width, 0f);
                _b.sizeDelta = new Vector2(_width, 0f);
                _a.anchoredPosition = Vector2.zero;
                _b.anchoredPosition = new Vector2(_width, 0f);
            }

            float step = Speed * Time.deltaTime;
            _a.anchoredPosition += new Vector2(-step, 0f);
            _b.anchoredPosition += new Vector2(-step, 0f);

            // 왼쪽으로 다 빠진 것은 오른쪽 끝으로 돌려보냅니다.
            if (_a.anchoredPosition.x <= -_width)
                _a.anchoredPosition = new Vector2(_b.anchoredPosition.x + _width, 0f);
            if (_b.anchoredPosition.x <= -_width)
                _b.anchoredPosition = new Vector2(_a.anchoredPosition.x + _width, 0f);
        }
    }
}
