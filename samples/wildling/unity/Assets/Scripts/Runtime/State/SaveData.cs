using System;
using System.Collections.Generic;

namespace Wildling.Game
{
    /// <summary>
    /// 세이브에 남는 것이다.
    /// </summary>
    /// <remarks>
    /// **표의 값을 복사해 두지 않습니다.** 레벨과 공명 등급만 남기고 능력치는 매번
    /// `GrowthCurve` 에서 계산합니다 — 밸런스를 고치고 다시 변환했을 때 기존 세이브에 그대로
    /// 반영되는 것이 이 도구를 쓰는 이유이기 때문입니다.
    ///
    /// 사전이 아니라 배열인 것은 유니티의 `JsonUtility` 가 사전을 다루지 않기 때문입니다.
    /// 외부 직렬화 라이브러리를 들이는 것보다 이쪽이 가볍습니다.
    /// </remarks>
    [Serializable]
    public sealed class SaveData
    {
        public int Version = 1;

        /// <summary>이 세이브가 어느 데이터로 만들어졌는가. 표가 바뀌면 눈으로 볼 수 있다.</summary>
        public string DataStamp = "";

        public List<Pair> Currencies = new();
        public List<Pair> Items = new();

        /// <summary>울림 조각이다. **종 단위입니다** — 각성해도 조각이 남아야 합니다.</summary>
        public List<Pair> Shards = new();

        /// <summary>공명 등급이다. 기획서 13.5 가 종 단위라고 정했으므로 개체가 아니다.</summary>
        public List<Pair> Resonances = new();

        public List<OwnedRow> Owned = new();
        public List<CodexRow> Codex = new();

        public List<string> UnlockedRegions = new();
        public List<Pair> RegionProgress = new();
        public List<string> FirstClears = new();
        public List<string> ClaimedCodexRewards = new();

        public List<PartyRow> Parties = new();
        public int ActiveParty;

        public string ExpeditionRegionId = "";
        public long ExpeditionStartedUtc;

        public long LastSeenUtc;
        public int NextUid = 1;

        [Serializable]
        public struct Pair
        {
            public string Key;
            public long Value;
        }

        [Serializable]
        public sealed class OwnedRow
        {
            public int Uid;
            public string MonsterId;
            public int Level = 1;
            public List<string> Active = new();
            public List<string> Passive = new();
            public List<int> SkillLevels = new();
        }

        [Serializable]
        public sealed class CodexRow
        {
            public string MonsterId;
            public int Observed;
            public int State;      // CodexState
        }

        [Serializable]
        public sealed class PartyRow
        {
            public List<int> Slots = new();
        }
    }
}
