using Tabbit.Rules;
using Tabbit.Validation;

// **정체가 발생하는 자리가 있는가.** 기획서 §10.4가 「각성 재료는 최소 2개 지역에서 산출」이라고
// 정했습니다. 한 지역에만 나오면 그 지역을 반복하는 것 외에 할 일이 없어지고, 방치형에서 그것은
// 이탈 지점입니다.
//
// 테이블 하나에 대한 질문이 아니라 **`RequirementEntry` 와 `RegionYield` 와 `RewardEntry` 를
// 함께 보아야 하는 질문**이므로 전역 규칙입니다.

internal static class ProgressionRules
{
    public static void Validate(IGlobalContext context)
    {
        // 각성이 요구하는 아이템.
        var required = new HashSet<string>();

        foreach (var row in Tables.RequirementEntry.Records)
        {
            if (row.Req is ItemRequirement item && !string.IsNullOrEmpty(item.ItemId))
                required.Add(item.ItemId);
        }

        // 아이템마다 그것을 내는 지역. 지역이 시간대마다 묶음을 가리키고, 묶음이 아이템을 냅니다.
        var byGroup = new Dictionary<string, HashSet<string>>();

        foreach (var row in Tables.RewardEntry.Records)
        {
            if (row.Reward is not ItemReward reward || string.IsNullOrEmpty(reward.ItemId))
                continue;

            if (!byGroup.TryGetValue(row.RewardGroupId, out var items))
            {
                items = new HashSet<string>();
                byGroup[row.RewardGroupId] = items;
            }

            items.Add(reward.ItemId);
        }

        var regionsOf = new Dictionary<string, HashSet<string>>();

        foreach (var row in Tables.RegionYield.Records)
        {
            if (!byGroup.TryGetValue(row.RewardGroupId, out var items))
                continue;

            foreach (string item in items)
            {
                if (!regionsOf.TryGetValue(item, out var regions))
                {
                    regions = new HashSet<string>();
                    regionsOf[item] = regions;
                }

                regions.Add(row.RegionId);
            }
        }

        context.Info($"각성 재료 {required.Count}종의 산출 지역을 검사합니다.");

        foreach (string item in required)
        {
            int count = regionsOf.TryGetValue(item, out var regions) ? regions.Count : 0;

            if (count == 0)
            {
                context.Error(
                    $"각성 재료 `{item}` 을 내는 지역이 없습니다 — 각성에 도달할 수 없습니다.");
            }
            else if (count < 2)
            {
                context.Warn(
                    $"각성 재료 `{item}` 이 지역 1개에서만 나옵니다 — 기획서 §10.4는 2개 이상을 "
                    + "요구합니다. 그 지역을 반복하는 것 외에 할 일이 없어집니다.");
            }
        }
    }
}
