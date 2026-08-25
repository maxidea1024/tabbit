using Tabbit.Rules;
using Tabbit.Validation;

// **지역마다 새로 발견할 것이 있는가.** 기획서 §11.3이 「각 지역에 고유 종이 최소 4종, 그중
// 1종은 은둔 슬롯」이라고 정했고, 지켜지지 않으면 새 지역을 해금해도 볼 것이 없습니다.
// 수집이 이 게임의 핵심이므로 그 조건은 밸런스가 아니라 성립 조건입니다.

internal static class EncounterTableRules
{
    public static void Validate(ITableContext context)
    {
        context.Info(
            $"EncounterTable {Tables.EncounterTable.Records.Count:N0}행을 검사합니다.");

        foreach (var row in Tables.EncounterTable.Records)
        {
            var entries = row.Entries;

            if (entries is null || entries.Length == 0)
            {
                context.Error(row, nameof(row.Entries), "출현 목록이 비어 있습니다.");
                continue;
            }

            var species = new HashSet<string>();
            int hidden = 0;

            foreach (var entry in entries)
            {
                var monster = entry.MonsterByMonsterId;
                if (monster is not null)
                    species.Add(monster.SpeciesId);

                if (entry.EncounterSlot == EncounterSlot.Hidden)
                    hidden++;

                if (entry.Weight <= 0)
                    context.Error(row, nameof(row.Entries), "가중치가 0 이하인 항목이 있습니다.");
            }

            if (species.Count < 4)
            {
                context.Error(row, nameof(row.Entries),
                    $"고유 종이 {species.Count}종입니다 — 4종 이상이어야 합니다.");
            }

            if (hidden == 0)
                context.Warn(row, nameof(row.Entries), "은둔 슬롯이 없습니다.");
        }
    }
}
