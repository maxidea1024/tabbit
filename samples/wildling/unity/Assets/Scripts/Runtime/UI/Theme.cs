using UnityEngine;
using Wildling.Data;

namespace Wildling.Game
{
    /// <summary>화면의 색과 글꼴이다.</summary>
    /// <remarks>
    /// **속성과 등급의 색은 아이콘을 만든 규칙과 같습니다.** 아이콘 생성기
    /// (`design-data/tools/art.py`)와 이 파일이 같은 값을 들고 있어야 화면이 한 벌로 보입니다.
    /// </remarks>
    public static class Theme
    {
        public static readonly Color Background = Rgb(0x16, 0x17, 0x1D);
        public static readonly Color Panel = Rgb(0x22, 0x24, 0x2E);
        public static readonly Color PanelHigh = Rgb(0x2C, 0x2F, 0x3C);
        public static readonly Color Line = Rgb(0x3A, 0x3E, 0x4E);

        /// <summary>위아래 띠이다. 판이 아니라 화면의 테두리이므로 두께를 주지 않는다.</summary>
        public static readonly Color Hud = Rgb(0x11, 0x12, 0x18);
        public static readonly Color Text = Rgb(0xE6, 0xE8, 0xF0);
        public static readonly Color TextDim = Rgb(0x9A, 0xA0, 0xB4);
        public static readonly Color Accent = Rgb(0x6C, 0xC6, 0xA0);
        public static readonly Color Warn = Rgb(0xE0, 0x7A, 0x6C);
        public static readonly Color Good = Rgb(0x7A, 0xC8, 0x8A);

        public static Color Element(Element element) => element switch
        {
            Data.Element.Flame => Rgb(0xE2, 0x54, 0x2C),
            Data.Element.Tide => Rgb(0x2E, 0x86, 0xC9),
            Data.Element.Leaf => Rgb(0x4F, 0xA8, 0x45),
            Data.Element.Arc => Rgb(0xE4, 0xBE, 0x2E),
            _ => Rgb(0x8B, 0x54, 0xC2),
        };

        public static Color Grade(Grade grade) => grade switch
        {
            Data.Grade.Common => Rgb(0x9A, 0xA5, 0xB1),
            Data.Grade.Rare => Rgb(0x4C, 0x8D, 0xE0),
            Data.Grade.Epic => Rgb(0xA6, 0x5C, 0xE0),
            Data.Grade.Legendary => Rgb(0xE0, 0xA6, 0x3C),
            _ => Rgb(0xE0, 0x5C, 0x7A),
        };

        public static string Label(Element element) => element switch
        {
            Data.Element.Flame => "불꽃",
            Data.Element.Tide => "물결",
            Data.Element.Leaf => "잎새",
            Data.Element.Arc => "번개",
            _ => "어둠",
        };

        public static string Label(Grade grade) => grade switch
        {
            Data.Grade.Common => "일반",
            Data.Grade.Rare => "희귀",
            Data.Grade.Epic => "영웅",
            Data.Grade.Legendary => "전설",
            _ => "신화",
        };

        public static string Label(Role role) => role switch
        {
            Role.Vanguard => "선봉",
            Role.Breaker => "파격",
            Role.Warden => "수호",
            _ => "조율",
        };

        public static string Label(TargetScope scope) => scope switch
        {
            TargetScope.Single => "적 1",
            TargetScope.AllEnemy => "적 전체",
            TargetScope.OneAlly => "아군 1",
            _ => "아군 전체",
        };

        public static string Label(StageKind kind) => kind switch
        {
            StageKind.Normal => "일반",
            StageKind.Observation => "관측",
            _ => "수호자",
        };

        private static Font _font;

        /// <summary>
        /// 한글이 나오는 글꼴이다.
        /// </summary>
        /// <remarks>
        /// **운영체제의 글꼴을 씁니다.** 유니티 6에는 내장 `Arial` 이 없고, 글꼴 파일을
        /// 저장소에 넣으면 배포 조건을 따로 확인해야 합니다. 목록의 앞에서부터 있는 것을
        /// 쓰고, 하나도 없으면 유니티의 기본 글꼴로 물러납니다.
        /// </remarks>
        public static Font Font
        {
            get
            {
                if (_font != null)
                    return _font;

                foreach (string name in new[]
                         {
                             "Malgun Gothic", "맑은 고딕", "NanumGothic", "Noto Sans KR",
                             "Apple SD Gothic Neo", "Arial Unicode MS", "Arial",
                         })
                {
                    var font = Font.CreateDynamicFontFromOSFont(name, 24);
                    if (font != null)
                        return _font = font;
                }

                return _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            }
        }

        private static Color Rgb(int r, int g, int b) => new(r / 255f, g / 255f, b / 255f);
    }
}
