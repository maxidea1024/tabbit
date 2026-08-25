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
        var orders = new Dictionary<string, HashSet<int>>();

        foreach (var row in Tables.RewardEntry.Records)
        {
            string group = row.RewardGroupId;

            sums.TryGetValue(group, out int sum);
            sums[group] = sum + row.Rate;

            if (!orders.TryGetValue(group, out var used))
            {
                used = new HashSet<int>();
                orders[group] = used;
            }

            if (!used.Add(row.Order))
                context.Error(row, nameof(row.Order), $"묶음 `{group}` 에서 순서가 겹칩니다.");

            // 조각 보상은 그 종의 조각입니다. 1단이 아닌 단계를 가리키면 조각의 임자가 종이
            // 아니라 단계처럼 보이게 되고, 공명 등급이 종 단위라는 규칙과 어긋납니다.
            if (row.Reward is global::Wildling.Data.ShardReward shard)
            {
                var monster = Tables.Monster.RecordsByMonsterId.TryGetValue(
                    shard.MonsterId?.MonsterId ?? "", out var found) ? found : null;

                if (monster is not null && monster.Stage != 1)
                {
                    context.Warn(row, nameof(row.Reward),
                        $"조각 보상이 {monster.Stage}단(`{monster.MonsterId}`)을 가리킵니다 — "
                        + "조각은 종 단위이므로 1단을 가리키는 편이 읽기 쉽습니다.");
                }
            }
        }

        foreach (var pair in sums)
        {
            if (pair.Value > 10000 * orders[pair.Key].Count)
                continue;

            // 확률의 합이 10000을 넘는 묶음. 항목마다 독립 확률인 표에서는 정상이므로 경고입니다.
            if (pair.Value > 10000)
            {
                context.Warn(
                    $"묶음 `{pair.Key}` 의 확률 합이 {pair.Value}입니다 — 항목마다 독립 확률이 "
                    + "아니라면 10000을 넘을 수 없습니다.");
            }
        }
    }
}
