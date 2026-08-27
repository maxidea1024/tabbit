using Tabbit.Rules;
using Tabbit.Validation;

// 이름은 한국어로 시트에 있고 영어는 `StringTable` 에 있습니다. 한쪽만 있으면 그 언어로
// 게임을 켰을 때 빈 칸이 나오고, 변환은 통과합니다.

internal static class TextRules
{
    public static void Validate(IGlobalContext context)
    {
        var strings = Tables.StringTable.Records.ToDictionary(row => row.StringId);

        Check(context, strings, "joker", Tables.Joker.Records.Select(row => row.JokerId));
        Check(context, strings, "tarot", Tables.Tarot.Records.Select(row => row.TarotId));
        Check(context, strings, "planet", Tables.Planet.Records.Select(row => row.PlanetId));
        Check(context, strings, "spectral",
              Tables.Spectral.Records.Select(row => row.SpectralId));
        Check(context, strings, "voucher",
              Tables.Voucher.Records.Select(row => row.VoucherId));
        Check(context, strings, "tag", Tables.Tag.Records.Select(row => row.TagId));
        Check(context, strings, "deck", Tables.Deck.Records.Select(row => row.DeckId));
        Check(context, strings, "boss", Tables.BossBlind.Records.Select(row => row.BossId));
        Check(context, strings, "achievement",
              Tables.Achievement.Records.Select(row => row.AchievementId));

        context.Info($"번역 {Tables.StringTable.Records.Count}줄을 검사합니다.");
    }

    private static void Check(
        IGlobalContext context, Dictionary<string, StringTableTable.Record> strings,
        string prefix, IEnumerable<string> ids)
    {
        var missing = new List<string>();

        foreach (string id in ids)
        {
            string key = $"{prefix}.{id}.name";

            if (!strings.TryGetValue(key, out var row))
            {
                missing.Add(id);
                continue;
            }

            if (string.IsNullOrWhiteSpace(row.Ko))
                context.Error(row, "ko", $"`{key}` 의 한국어가 비어 있습니다.");

            if (string.IsNullOrWhiteSpace(row.En))
                context.Error(row, "en", $"`{key}` 의 영어가 비어 있습니다.");
        }

        if (missing.Count > 0)
        {
            context.Error(
                $"`{prefix}` {missing.Count}개가 번역 대조본에 없습니다: "
                + string.Join(", ", missing.Take(5)));
        }
    }
}
