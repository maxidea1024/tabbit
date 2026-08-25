using Tabbit.Rules;
using Tabbit.Validation;

// 확률의 합. 묶음 하나의 항목들이 10000을 넘으면 그 표는 「반드시 나오는 것」이 둘 이상이라는
// 뜻이 되고, 읽는 쪽이 무엇을 의도했는지 알 수 없습니다.
//
// 그리고 **변종이 가리키는 카탈로그가 맞는가.** 판별자가 형태를 정하지만, `ShardReward` 가
// 가리키는 종이 실제로 조각을 낼 수 있는 종인지는 시트가 말하지 않습니다.

internal static class RewardEntryRules
{
    public static void Validate(ITableContext context)
    {
        context.Info($"RewardEntry {Tables.RewardEntry.Records.Count:N0}행을 검사합니다.");

        var sums = new Dictionary<string, int>();
        var guaranteed = new HashSet<string>();
        var orders = new Dictionary<string, HashSet<int>>();

        foreach (var row in Tables.RewardEntry.Records)
        {
            string group = row.RewardGroupId;

            sums.TryGetValue(group, out int sum);
            sums[group] = sum + row.Rate;

            if (row.Rate >= 10000)
                guaranteed.Add(group);

            if (!orders.TryGetValue(group, out var used))
            {
                used = new HashSet<int>();
                orders[group] = used;
            }

            if (!used.Add(row.Order))
                context.Error(row, nameof(row.Order), $"묶음 `{group}` 에서 순서가 겹칩니다.");

            // 조각 보상은 그 종의 조각입니다. 1단이 아닌 단계를 가리키면 조각의 임자가 종이
            // 아니라 단계처럼 보이게 되고, 공명 등급이 종 단위라는 규칙과 어긋납니다.
            if (row.Reward is ShardReward shard)
            {
                var monster = Tables.Monster.RecordsByMonsterId.TryGetValue(
                    shard.MonsterId ?? "", out var found) ? found : null;

                if (monster is not null && monster.Stage != 1)
                {
                    context.Warn(row, nameof(row.Reward),
                        $"조각 보상이 {monster.Stage}단(`{monster.MonsterId}`)을 가리킵니다 — "
                        + "조각은 종 단위이므로 1단을 가리키는 편이 읽기 쉽습니다.");
                }
            }
        }

        // **합을 검사하지 않습니다.** 이 데이터의 묶음은 항목마다 독립 확률입니다 — 골드는
        // 항상 나오고 재료는 절반, 원석은 드물게. 그런 표에서 합이 10000을 넘는 것은 정상이고,
        // 처음에는 합을 검사했다가 90건이 울렸습니다. 도구가 「이만큼 보고하는 규칙은 보통
        // 규칙이 틀린 것」이라고 말해 주었고, 그 말이 맞았습니다.
        //
        // 뜻이 있는 것은 둘입니다 — 아무것도 나오지 않을 수 있는 묶음, 그리고 가리키는 항목이
        // 없는 묶음.
        foreach (var group in Tables.RewardGroup.Records)
        {
            if (!sums.ContainsKey(group.RewardGroupId))
            {
                context.Warn(group, nameof(group.RewardGroupId),
                    "항목이 하나도 없는 묶음입니다 — 이것을 가리키는 쪽은 아무것도 받지 못합니다.");
                continue;
            }

            if (!guaranteed.Contains(group.RewardGroupId))
            {
                context.Warn(group, nameof(group.RewardGroupId),
                    "항목이 전부 확률입니다 — 아무것도 나오지 않는 경우가 생깁니다.");
            }
        }
    }
}
