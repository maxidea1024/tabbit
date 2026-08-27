using Tabbit.Rules;
using Tabbit.Validation;

// 무엇이 빠졌는가를 봅니다. 이 샘플은 원작을 재현하는 것이므로 **개수가 규격**이고, 개수가
// 어긋나면 옮기다 빠뜨린 것입니다. 타입으로는 표현되지 않습니다.

internal static class CoverageRules
{
    public static void Validate(IGlobalContext context)
    {
        Rarities(context);
        EveryJokerHasAnEffect(context);
        PlanetsCoverEveryHand(context);
        VouchersComeInPairs(context);
    }

    // 커먼 61 · 언커먼 64 · 레어 20 · 전설 5. doc/parity/jokers.md 의 표입니다.
    private static void Rarities(IGlobalContext context)
    {
        foreach (var weight in Tables.JokerRarityWeight.Records)
        {
            int actual = Tables.Joker.Records.Count(row => row.Rarity == weight.Rarity);
            if (actual != weight.Count)
            {
                context.Error(weight, nameof(weight.Count),
                    $"`{weight.Rarity}` 이 {weight.Count}종이어야 하는데 {actual}종입니다.");
            }
        }

        context.Info($"조커 {Tables.Joker.Records.Count}종을 검사합니다.");
    }

    // 효과 행이 없는 조커는 상점에 나오지만 아무것도 하지 않습니다. 변환은 통과합니다.
    private static void EveryJokerHasAnEffect(IGlobalContext context)
    {
        var owners = Tables.JokerEffect.Records.Select(row => row.Owner).ToHashSet();

        foreach (var joker in Tables.Joker.Records)
        {
            if (!owners.Contains(joker.JokerId))
                context.Error(joker, nameof(joker.JokerId), "효과 행이 하나도 없습니다.");
        }
    }

    // 행성 12종이 족보 12종과 일대일입니다. 하나가 겹치면 어느 족보는 올릴 방법이 없습니다.
    private static void PlanetsCoverEveryHand(IGlobalContext context)
    {
        var covered = new Dictionary<PokerHandKind, string>();

        foreach (var planet in Tables.Planet.Records)
        {
            if (covered.TryGetValue(planet.Hand, out string other))
            {
                context.Error(planet, nameof(planet.Hand),
                    $"`{other}` 와 같은 족보를 올립니다.");
                continue;
            }

            covered[planet.Hand] = planet.PlanetId;
        }

        foreach (var hand in Tables.PokerHand.Records)
        {
            if (!covered.ContainsKey(hand.Hand))
                context.Error(hand, nameof(hand.Hand), "이 족보를 올리는 행성이 없습니다.");
        }
    }

    // 32종이 16쌍입니다. 상위가 없는 하위와 하위가 없는 상위는 상점에 나오지 않거나 조건 없이
    // 나옵니다.
    private static void VouchersComeInPairs(IGlobalContext context)
    {
        var upgrades = new Dictionary<string, string>();

        foreach (var voucher in Tables.Voucher.Records)
        {
            if (string.IsNullOrEmpty(voucher.UpgradesFrom))
                continue;

            if (upgrades.TryGetValue(voucher.UpgradesFrom, out string other))
            {
                context.Error(voucher, nameof(voucher.UpgradesFrom),
                    $"`{other}` 도 같은 하위를 잇습니다.");
            }

            upgrades[voucher.UpgradesFrom] = voucher.VoucherId;
        }

        int bases = Tables.Voucher.Records.Count(row => string.IsNullOrEmpty(row.UpgradesFrom));
        if (bases != upgrades.Count)
        {
            context.Error(
                $"하위 {bases}종에 상위 {upgrades.Count}종입니다 — 쌍이 맞지 않습니다.");
        }
    }
}
