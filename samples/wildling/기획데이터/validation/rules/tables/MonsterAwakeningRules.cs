using Tabbit.Rules;
using Tabbit.Validation;

// **각성이 손실이 되지 않는가.** 기획서 §7.3이 정한 것이고 타입으로는 표현되지 않습니다 —
// 다음 단계의 1레벨 기준값이 직전 단계보다 낮으면 플레이어는 각성한 뒤 약해집니다. 각성은
// 이 게임의 수집 목표이므로, 그 자리에서 손실이 나면 시스템이 성립하지 않습니다.
//
// 그리고 `gain` 이 실제 차이와 같은가. 두 값이 서로 다른 자리에 적혀 있으므로 **한쪽만 고치는
// 것**이 이 데이터에서 가장 일어나기 쉬운 실수입니다.

internal static class MonsterAwakeningRules
{
    public static void Validate(ITableContext context)
    {
        context.Info(
            $"MonsterAwakening {Tables.MonsterAwakening.Records.Count:N0}행을 검사합니다.");

        foreach (var row in Tables.MonsterAwakening.Records)
        {
            var from = row.MonsterByFromMonsterId;
            var to = row.MonsterByToMonsterId;

            if (from is null || to is null)
                continue;

            if (from.SpeciesId != to.SpeciesId)
            {
                context.Error(row, nameof(row.ToMonsterId),
                    $"종이 다릅니다 — `{from.SpeciesId}` 에서 `{to.SpeciesId}` 로 각성합니다.");
            }

            if (to.Stage != from.Stage + 1)
            {
                context.Error(row, nameof(row.ToMonsterId),
                    $"{from.Stage}단에서 {to.Stage}단으로 갑니다 — 각성은 한 단계씩입니다.");
            }

            Compare(context, row, "hp", from.Base.Hp, to.Base.Hp, row.Gain.Hp);
            Compare(context, row, "attack", from.Base.Attack, to.Base.Attack, row.Gain.Attack);
            Compare(context, row, "defense", from.Base.Defense, to.Base.Defense, row.Gain.Defense);

            if (row.Costs is null || row.Costs.Length == 0)
                context.Warn(row, nameof(row.Costs), "소모 재화가 없습니다 — 각성이 공짜입니다.");
        }
    }

    private static void Compare(
        ITableContext context, Tables.MonsterAwakening.Record row,
        string stat, int before, int after, int gain)
    {
        if (after < before)
        {
            context.Error(row, nameof(row.ToMonsterId),
                $"각성 후 `{stat}` 이 {before}에서 {after}로 낮아집니다.");
        }

        if (gain != after - before)
        {
            context.Warn(row, nameof(row.Gain),
                $"`gain.{stat}` 이 {gain}인데 실제 차이는 {after - before}입니다.");
        }
    }
}
