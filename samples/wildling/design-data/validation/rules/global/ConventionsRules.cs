using Tabbit.Rules;
using Tabbit.Validation;

// 전역 규칙은 테이블 하나에 대한 질문이 아닌 것을 위한 자리입니다. `Schema` 는 열거하고,
// 규약은 그것을 필요로 합니다 — 「모든 테이블」은 타입이 있는 `Tables` 에게 물을 수 없습니다.

internal static class ConventionsRules
{
    public static void Validate(IGlobalContext context)
    {
        int tables = context.Schema.Tables.Count;
        int columns = context.Schema.Tables.Sum(table => table.Fields.Count);

        context.Info($"테이블 {tables}개, 컬럼 {columns}개를 검사합니다.");

        foreach (var table in context.Schema.Tables)
        {
            // 이 프로젝트는 컬럼 설명을 전부 채웁니다 — `.tsv` 의 `:desc` 줄이 그 자리입니다.
            // 그래서 이 규칙은 지금 조용하고, 설명 없는 컬럼이 하나 들어오는 날 그것만 짚습니다.
            foreach (var field in table.Fields)
            {
                if (string.IsNullOrWhiteSpace(field.Comment))
                    context.Warn(field, $"`{table.Name}.{field.Name}` 에 설명이 없습니다.");
            }

            if (string.IsNullOrWhiteSpace(table.Comment))
                context.Warn($"테이블 `{table.Name}` 에 설명이 없습니다.");
        }

        // 아이콘 이름의 접두어 규약. `asset` 검사는 파일이 있는지만 보고, 이름의 규약은 보지
        // 않습니다 — 그것은 프로젝트가 정하는 것이고, 그래서 이 자리에 옵니다.
        Prefix(context, "와일드링", Tables.Monster.Records.Select(row => row.Icon), "wl_");
        Prefix(context, "스킬", Tables.Skill.Records.Select(row => row.Icon), "sk_");
        Prefix(context, "아이템", Tables.Item.Records.Select(row => row.Icon), "it_");
        Prefix(context, "재화", Tables.Currency.Records.Select(row => row.Icon), "cur_");
    }

    private static void Prefix(
        IGlobalContext context, string what, IEnumerable<string> icons, string prefix)
    {
        var offenders = icons
            .Where(icon => !string.IsNullOrEmpty(icon) && !icon.StartsWith(prefix))
            .Distinct()
            .ToList();

        if (offenders.Count == 0)
            return;

        context.Warn(
            $"{what} 아이콘 {offenders.Count}개가 `{prefix}` 로 시작하지 않습니다: "
            + string.Join(", ", offenders.Take(5)));
    }
}
