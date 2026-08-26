using System.Collections.Generic;
using UnityEngine;
using Wildling.Data;

namespace Wildling.Game
{
    /// <summary>
    /// 화면의 껍데기이다 — 9분할 판·버튼·테두리·빛.
    /// </summary>
    /// <remarks>
    /// **외부 애셋을 쓰지 않습니다.** 애셋스토어 패키지는 로그인이 필요하고 재배포가 막혀
    /// 저장소에 담을 수 없으므로, 클론한 사람이 빌드할 수 없게 됩니다. `UIEffect` 같은 공개
    /// 패키지도 `Object.GetInstanceID()` 가 이 에디터에서 오류로 승격되어 컴파일되지 않았습니다.
    ///
    /// 그래서 아이콘과 같은 파이프라인(`design-data/tools/art_ui.py`)으로 만들고 여기서
    /// 이름으로 찾습니다.
    /// </remarks>
    public static class Skin
    {
        private static readonly Dictionary<string, Sprite> Cache = new();

        public static Sprite Get(string name)
        {
            if (Cache.TryGetValue(name, out var cached))
                return cached;
            var sprite = Resources.Load<Sprite>("art/ui/" + name);
            Cache[name] = sprite;
            return sprite;
        }

        public static Sprite Panel => Get("panel");
        public static Sprite PanelSoft => Get("panel_soft");
        public static Sprite PanelSunk => Get("panel_sunk");
        public static Sprite Button => Get("button");
        public static Sprite ButtonAccent => Get("button_accent");
        public static Sprite ButtonWarn => Get("button_warn");
        public static Sprite BarBack => Get("bar_back");
        public static Sprite BarFill => Get("bar_fill");
        public static Sprite Glow => Get("glow");
        public static Sprite Spark => Get("spark");
        public static Sprite Shine => Get("shine");
        public static Sprite Sheen => Get("sheen");

        public static Sprite Frame(Grade grade) => grade switch
        {
            Grade.Common => Get("frame_common"),
            Grade.Rare => Get("frame_rare"),
            Grade.Epic => Get("frame_epic"),
            Grade.Legendary => Get("frame_legendary"),
            _ => Get("frame_mythic"),
        };

        /// <summary>
        /// 그 색으로 칠하려던 판을 어느 껍데기로 바꿀 것인가.
        /// </summary>
        /// <remarks>
        /// 화면 코드는 여전히 색으로 말합니다 — `Theme.Panel` 을 넘기면 이 표가 그것을 판
        /// 그림으로 바꿉니다. 그래서 부르는 자리를 하나도 고치지 않고 껍데기가 입혀집니다.
        /// </remarks>
        public static Sprite PanelFor(Color color)
        {
            if (Same(color, Theme.Panel))
                return Panel;
            if (Same(color, Theme.PanelHigh))
                return PanelSoft;
            return null;
        }

        public static Sprite ButtonFor(Color color)
        {
            if (Same(color, Theme.Accent))
                return ButtonAccent;
            if (Same(color, Theme.Warn))
                return ButtonWarn;
            // 목록의 줄은 누를 수 있어도 판처럼 보여야 합니다.
            if (Same(color, Theme.Panel))
                return Panel;
            return Button;
        }

        private static bool Same(Color a, Color b)
            => Mathf.Abs(a.r - b.r) < 0.004f
               && Mathf.Abs(a.g - b.g) < 0.004f
               && Mathf.Abs(a.b - b.b) < 0.004f;
    }
}
