using System.Collections.Generic;
using System.Linq;
using Wildling.Data;

namespace Wildling.Game
{
    /// <summary>
    /// 조건 묶음을 확인한다.
    /// </summary>
    /// <remarks>
    /// **주어가 있는 조건과 없는 조건이 섞여 있습니다.** 각성의 「레벨 20」은 각성할 개체의
    /// 레벨이고, 지역 해금의 「스테이지 클리어」는 주어가 없습니다. 그래서
    /// <paramref name="subject"/> 가 없을 수 있고, 없는데 주어를 요구하는 조건이 나오면
    /// 만족하지 않은 것으로 봅니다.
    /// </remarks>
    public static class Requirements
    {
        public struct Check
        {
            public string Text;
            public bool Met;
        }

        public static IEnumerable<RequirementEntryTable.Record> Entries(string groupId)
            => string.IsNullOrEmpty(groupId)
                ? Enumerable.Empty<RequirementEntryTable.Record>()
                : WildlingData.RequirementEntry.Records
                    .Where(r => r.RequirementGroupId == groupId)
                    .OrderBy(r => r.Order);

        public static bool Met(string groupId, GameState state, Owned subject)
            => Evaluate(groupId, state, subject).All(c => c.Met);

        public static List<Check> Evaluate(string groupId, GameState state, Owned subject)
        {
            var checks = new List<Check>();

            foreach (var entry in Entries(groupId))
            {
                switch (entry.Req)
                {
                    case LevelRequirement level:
                        checks.Add(new Check
                        {
                            Text = $"레벨 {level.Level}",
                            Met = subject != null && subject.Level >= level.Level,
                        });
                        break;

                    case CodexRequirement codex:
                        checks.Add(new Check
                        {
                            Text = $"기록 {Label(codex.CodexState)}",
                            Met = subject != null
                                  && state.CodexState(subject.MonsterId) >= codex.CodexState,
                        });
                        break;

                    case CodexCompletionRequirement completion:
                    {
                        // 완성률은 주어가 없습니다 — 기록부 전체의 비율입니다.
                        int have = state.Completion();
                        checks.Add(new Check
                        {
                            Text = $"기록부 {completion.Percent}% "
                                   + $"(지금 {Numbers.AsPercent(have)})",
                            Met = have >= completion.Percent * (Numbers.One / 100),
                        });
                        break;
                    }

                    case ItemRequirement item:
                    {
                        // **참조는 이름 둘을 냅니다.** `ItemId` 가 키이고 `ItemByItemId` 가 행입니다.
                        string name = item.ItemByItemId?.Name ?? item.ItemId;
                        checks.Add(new Check
                        {
                            Text = $"{name} {item.Amount}",
                            Met = state.ItemCount(item.ItemId) >= item.Amount,
                        });
                        break;
                    }

                    case StageRequirement stage:
                    {
                        var row = stage.StageByStageId;
                        string name = row is null
                            ? stage.StageId
                            : $"{row.RegionByRegionId?.Name ?? row.RegionId} {row.Index}";
                        checks.Add(new Check
                        {
                            Text = $"{name} 클리어",
                            Met = state.IsCleared(stage.StageId),
                        });
                        break;
                    }
                }
            }

            return checks;
        }

        public static string Label(CodexState state) => state switch
        {
            CodexState.Unknown => "미기록",
            CodexState.Sighted => "목격",
            CodexState.Recorded => "기록",
            _ => "정독",
        };
    }
}
