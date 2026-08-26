using Tabbit.Rules;
using Tabbit.Validation;

// 시트가 말할 수 없는 것 넷입니다 — 종과 단계의 조합, 단계가 이어지는가, `max_stage` 와 실제
// 사슬의 일치, 그리고 서식 지역이 하나라도 있는가.

internal static class MonsterRules
{
    public static void Validate(ITableContext context)
    {
        context.Info($"Monster {Tables.Monster.Records.Count:N0}행을 검사합니다.");

        // 기본 인덱스는 `monster_id` 이므로 (종, 단계) 조합의 유일성은 검사되지 않습니다.
        // 그 조합이 겹치면 각성 사슬이 어느 행으로 가는지가 갈립니다.
        var seen = new Dictionary<string, string>();

        // 종마다 실제로 존재하는 단계. `max_stage` 가 그것과 맞아야 합니다 — 어긋나면 기록부의
        // 「최대 성장 단계」가 도달할 수 없는 값을 표시합니다.
        var stages = new Dictionary<string, List<int>>();

        foreach (var row in Tables.Monster.Records)
        {
            string key = row.SpeciesId + "#" + row.Stage;
            if (seen.TryGetValue(key, out string first))
            {
                context.Error(row, nameof(row.Stage),
                    $"종 `{row.SpeciesId}` 의 {row.Stage}단이 두 번 있습니다 — `{first}` 와 겹칩니다.");
            }
            else
            {
                seen[key] = row.MonsterId;
            }

            if (!stages.TryGetValue(row.SpeciesId, out var list))
            {
                list = new List<int>();
                stages[row.SpeciesId] = list;
            }

            list.Add(row.Stage);

            if (row.Stage > row.MaxStage)
            {
                context.Error(row, nameof(row.MaxStage),
                    $"{row.Stage}단인데 `max_stage` 가 {row.MaxStage}입니다.");
            }

            // `bitset` 이 0이면 어느 지역에도 서식하지 않는다는 뜻이고, 그러면 기록부의 「발견
            // 지역」이 비고 탐사로 만날 수 없습니다.
            if (row.Habitat == 0)
                context.Error(row, nameof(row.Habitat), "서식 지역이 하나도 없습니다.");
        }

        foreach (var pair in stages)
        {
            var list = pair.Value;
            list.Sort();

            // 1단이 없는 종은 획득 경로가 없습니다.
            if (list[0] != 1)
            {
                context.Error($"종 `{pair.Key}` 의 첫 단계가 {list[0]}단입니다 — 1단이 없습니다.");
                continue;
            }

            // 단계가 이어져야 합니다. 2단 없이 3단이 있으면 각성으로 도달할 수 없습니다.
            for (int i = 1; i < list.Count; i++)
            {
                if (list[i] != list[i - 1] + 1)
                {
                    context.Error(
                        $"종 `{pair.Key}` 의 단계가 {list[i - 1]}단에서 {list[i]}단으로 건너뜁니다.");
                }
            }
        }
    }
}
