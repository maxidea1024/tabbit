using Tabbit.Rules;
using Tabbit.Validation;

// 짝을 이루는 두 배열의 길이. 배열 하나의 길이는 타입이 말하지만, **둘이 같아야 한다**는 것은
// 시트에 적을 자리가 없습니다.

internal static class StageRules
{
    public static void Validate(ITableContext context)
    {
        context.Info($"Stage {Tables.Stage.Records.Count:N0}행을 검사합니다.");

        foreach (var row in Tables.Stage.Records)
        {
            int monsters = row.WaveMonsterIds?.Length ?? 0;
            int levels = row.WaveLevels?.Length ?? 0;

            if (monsters == 0)
            {
                context.Error(row, nameof(row.WaveMonsterIds), "등장 목록이 비어 있습니다.");
                continue;
            }

            if (monsters != levels)
            {
                context.Error(row, nameof(row.WaveLevels),
                    $"등장이 {monsters}개인데 레벨이 {levels}개입니다.");
            }

            // 수호자 스테이지는 지역의 마지막이어야 합니다 — 그 뒤에 스테이지가 있으면 해금
            // 순서가 성립하지 않습니다.
            if (row.StageKind == global::Wildling.Data.StageKind.Guardian && row.Index != 18)
            {
                context.Error(row, nameof(row.StageKind),
                    $"수호자 스테이지가 {row.Index}번입니다 — 지역의 마지막이어야 합니다.");
            }
        }
    }
}
