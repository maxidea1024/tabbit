using System.Collections.Generic;
using UnityEngine;

namespace Wildling.Game
{
    /// <summary>
    /// `asset=` 컬럼이 적은 이름으로 그림을 찾는다.
    /// </summary>
    /// <remarks>
    /// **표가 이름을 정하고 이 클래스가 그 이름으로 찾습니다.** 코드 어디에도 파일 이름이
    /// 적혀 있지 않으므로, 종이 늘거나 아이콘 이름이 바뀌어도 고칠 자리가 없습니다.
    ///
    /// 그림이 `Resources/` 아래인 것은 실행 중에 이름으로 찾기 때문입니다 — 그 폴더가 아니면
    /// 빌드에 들어가지 않습니다.
    /// </remarks>
    public static class ArtLibrary
    {
        private static readonly Dictionary<string, Sprite> Cache = new();

        /// <summary>찾지 못한 이름이다. 자동 플레이 검사가 이것을 읽는다.</summary>
        public static readonly List<string> Missing = new();

        public static Sprite Icon(string name) => Load("art/icon/", name);

        public static Sprite Model(string name) => Load("art/model/", name);

        private static Sprite Load(string folder, string name)
        {
            if (string.IsNullOrEmpty(name))
                return null;

            string path = folder + name;
            if (Cache.TryGetValue(path, out var cached))
                return cached;

            var sprite = Resources.Load<Sprite>(path);
            if (sprite is null && !Missing.Contains(path))
                Missing.Add(path);

            Cache[path] = sprite;
            return sprite;
        }

        /// <summary>표가 가리키는 그림을 전부 로드해 본다. 없는 것의 수를 낸다.</summary>
        public static int LoadEverythingTheTablesPointAt()
        {
            Missing.Clear();

            foreach (var row in Data.WildlingData.Monster.Records)
                Icon(row.Icon);
            foreach (var row in Data.WildlingData.Skill.Records)
                Icon(row.Icon);
            foreach (var row in Data.WildlingData.Item.Records)
                Icon(row.Icon);
            foreach (var row in Data.WildlingData.Currency.Records)
                Icon(row.Icon);
            foreach (var row in Data.WildlingData.Region.Records)
                Model(row.Background);

            return Missing.Count;
        }
    }
}
