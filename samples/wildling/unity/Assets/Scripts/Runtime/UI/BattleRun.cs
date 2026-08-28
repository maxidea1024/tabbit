using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Wildling.Data;

namespace Wildling.Game
{
    /// <summary>
    /// 자동 전투가 이어지는 동안의 누적이다.
    /// </summary>
    /// <remarks>
    /// **방치형이므로 전투는 한 판으로 끝나지 않습니다.** 기획서 9.2 가 「배속 · 반복 횟수 ·
    /// 패배 시 중단」을 전투 전에 정하는 것으로 두었습니다. 판이 바뀌어도 이 객체가 남아
    /// 그동안 쌓인 것을 들고 다닙니다.
    /// </remarks>
    public sealed class BattleRun
    {
        public int Wins;
        public int Losses;
        public int Rounds;

        /// <summary>이 연속 전투에서 받은 것 전부.</summary>
        public readonly List<Grant> Tally = new();

        public readonly List<string> Opened = new();
        public readonly List<string> NewMonsters = new();

        /// <summary>
        /// 지면 어떻게 하는가.
        /// </summary>
        /// <remarks>
        /// **기본은 물러나서 계속 도는 것입니다.** 방치형에서 벽에 막혔을 때 할 일은 멈추는 것이
        /// 아니라 넘을 수 있는 자리를 반복해 재화와 조각을 쌓는 것입니다. 기획서 9.2 의
        /// 「패배 시 중단」은 그것을 끄는 설정으로 두었습니다.
        /// </remarks>
        public static bool StopOnLose;

        /// <summary>막혀서 물러난 자리. 없으면 빈 문자열이다.</summary>
        public string FellBackTo = "";

        /// <summary>사람이 멈춘 것인가.</summary>
        public bool Stopped;

        public void Add(IEnumerable<Grant> grants)
        {
            Tally.AddRange(grants);
            var merged = Rewards.Merge(Tally);
            Tally.Clear();
            Tally.AddRange(merged);
        }

        /// <summary>
        /// 다음에 돌 스테이지를 고른다. 없으면 빈 문자열이다.
        /// </summary>
        /// <remarks>
        /// 아직 깨지 않은 것이 있으면 그것으로 넘어가고, 그 지역을 다 깼으면 **같은 자리를
        /// 반복합니다** — 방치의 목적이 진행만이 아니라 재화와 조각을 쌓는 것이기 때문입니다.
        /// </remarks>
        public static string NextStage(GameState state, StageRecord current)
        {
            if (current is null)
                return "";

            var ahead = WildlingData.Stage.Records
                .Where(s => s.RegionId == current.RegionId)
                .Where(s => !state.IsCleared(s.StageId) && state.IsStageOpen(s))
                .OrderBy(s => s.Index)
                .FirstOrDefault();

            return ahead?.StageId ?? current.StageId;
        }

        /// <summary>
        /// 졌을 때 물러날 자리이다.
        /// </summary>
        /// <remarks>
        /// 깬 것 중 가장 뒤가 벌이가 가장 좋습니다. 하나도 못 깼으면 첫 스테이지입니다 —
        /// 거기서도 지면 파티를 고쳐야 하는 것이고, 그 사실이 결과 화면에 적힙니다.
        /// </remarks>
        public static string FallbackStage(GameState state, StageRecord current)
        {
            if (current is null)
                return "";

            var stages = WildlingData.Stage.Records
                .Where(s => s.RegionId == current.RegionId)
                .OrderBy(s => s.Index)
                .ToList();

            var cleared = stages.LastOrDefault(s => state.IsCleared(s.StageId));
            return cleared?.StageId ?? stages.FirstOrDefault()?.StageId ?? current.StageId;
        }
    }

    /// <summary>일정한 간격으로 부르는 것이다.</summary>
    /// <remarks>
    /// 방치형의 화면은 손대지 않아도 값이 흘러야 합니다. 화면 전체를 다시 조립하면 스크롤이
    /// 튀므로, 바뀌는 글자와 막대만 이것으로 갱신합니다.
    /// </remarks>
    public sealed class Ticker : MonoBehaviour
    {
        public float Interval = 1f;
        public System.Action Tick;

        private float _next;

        private void Update()
        {
            if (Tick is null || Time.unscaledTime < _next)
                return;
            _next = Time.unscaledTime + Interval;
            Tick();
        }
    }
}
