using Tabbit.Rules;
using Tabbit.Validation;

// 효과 행이 스스로 모순되지 않는가. **타입은 변종의 필드가 채워졌는지까지는 보지 않습니다** —
// `PerUnit` 이 덱의 9를 세라고 하면서 어느 랭크인지 비워 두면, 변환은 통과하고 두 구현이
// 각자 다른 짐작을 합니다.

internal static class EffectRules
{
    // `Custom` 으로 남긴 것들. **doc/effect-vm.md 에 적힌 목록과 같아야 합니다** — 여기 없는
    // 이름이 시트에 들어오면 문서에 없는 처리를 두 구현이 각자 만들게 됩니다.
    private static readonly string[] KnownHandlers =
    {
        "pruning_shears",
    };

    public static void Validate(IGlobalContext context)
    {
        int custom = 0;

        foreach (var row in AllEffects())
        {
            PerUnitHasItsUnitField(context, row);

            if (row.Operation is OpCustom handler)
            {
                custom++;
                if (!KnownHandlers.Contains(handler.Handler))
                {
                    context.Error(row, nameof(row.Operation),
                        $"`{handler.Handler}` 가 문서의 `Custom` 목록에 없습니다.");
                }
            }
        }

        // 지표입니다. 0 이 되면 효과가 전부 데이터에 있는 것이고, 그때 두 구현이 갈라질 수
        // 있는 자리가 남지 않습니다.
        context.Info($"`Custom` 이 {custom}건입니다.");
    }

    private static IEnumerable<dynamic> AllEffects()
    {
        foreach (var row in Tables.JokerEffect.Records) yield return row;
        foreach (var row in Tables.TarotEffect.Records) yield return row;
        foreach (var row in Tables.SpectralEffect.Records) yield return row;
        foreach (var row in Tables.BossEffect.Records) yield return row;
        foreach (var row in Tables.VoucherEffect.Records) yield return row;
        foreach (var row in Tables.TagEffect.Records) yield return row;
        foreach (var row in Tables.DeckEffect.Records) yield return row;
        foreach (var row in Tables.EnhancementEffect.Records) yield return row;
        foreach (var row in Tables.SealEffect.Records) yield return row;
    }

    // 단위 셋은 「무엇을 세는가」를 보조 칸에서 읽습니다. 비어 있으면 셀 것이 정해지지
    // 않습니다.
    //
    // `ranks` 는 변종의 멤버가 아니라 행의 칸입니다 — 그 이유는 `doc/tool-findings.md` 에
    // 있습니다.
    private static void PerUnitHasItsUnitField(IGlobalContext context, dynamic row)
    {
        if (row.Operation is not OpPerUnit per)
            return;

        switch (per.Unit)
        {
            case UnitKind.DeckRankCount when row.Ranks is null || row.Ranks.Length == 0:
                context.Error(row, "ranks",
                    "`DeckRankCount` 인데 어느 랭크를 세는지 비어 있습니다.");
                break;

            case UnitKind.DeckEnhancementCount when per.Enhancement == EnhancementKind.None:
                context.Error(row, "operation.enhancement",
                    "`DeckEnhancementCount` 인데 어느 강화를 세는지 비어 있습니다.");
                break;

            case UnitKind.JokerRarityCount when (int)per.Rarity == 0:
                context.Error(row, "operation.rarity",
                    "`JokerRarityCount` 인데 어느 희귀도를 세는지 비어 있습니다.");
                break;
        }
    }
}
