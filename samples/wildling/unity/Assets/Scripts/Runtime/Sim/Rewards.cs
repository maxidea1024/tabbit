using System.Collections.Generic;
using System.Linq;
using Wildling.Data;

namespace Wildling.Game
{
    /// <summary>지급할 것 하나이다.</summary>
    public struct Grant
    {
        public GrantKind Kind;
        public string Id;
        public int Amount;

        public override string ToString() => $"{Kind}:{Id}×{Amount}";
    }

    public enum GrantKind
    {
        Item,
        Currency,
        Monster,
        Shard,
    }

    /// <summary>
    /// 보상 묶음을 굴린다.
    /// </summary>
    /// <remarks>
    /// **`RewardGroup` 하나를 8개 테이블이 가리킵니다.** 스테이지 클리어도, 탐사 산출도,
    /// 기록부 완성도, 의뢰도 같은 묶음 표를 씁니다 — 그래서 지급하는 코드가 여기 하나입니다.
    ///
    /// `RewardEntry.reward` 가 다형이므로 변종별로 갈립니다. **변종의 참조는 이름 둘을
    /// 냅니다** — `ItemId` 는 키이고 `ItemByItemId` 는 행입니다. 표시할 이름이 필요하면
    /// 뒤쪽입니다.
    /// </remarks>
    public static class Rewards
    {
        /// <summary>그 묶음의 항목을 순서대로 낸다. 굴리지 않고 보여 줄 때 쓴다.</summary>
        public static IEnumerable<RewardEntryTable.Record> Entries(string groupId)
            => string.IsNullOrEmpty(groupId)
                ? Enumerable.Empty<RewardEntryTable.Record>()
                : WildlingData.RewardEntry.Records
                    .Where(r => r.RewardGroupId == groupId)
                    .OrderBy(r => r.Order);

        /// <summary>확률을 굴려 실제로 나온 것만 낸다.</summary>
        public static List<Grant> Roll(string groupId, Rng rng, int times = 1)
        {
            var grants = new List<Grant>();
            var entries = Entries(groupId).ToList();

            for (int i = 0; i < times; i++)
            {
                foreach (var entry in entries)
                {
                    if (!rng.Chance(entry.Rate))
                        continue;
                    grants.Add(ToGrant(entry.Reward));
                }
            }
            return Merge(grants);
        }

        /// <summary>확률을 무시하고 전부 낸다. 첫 클리어 보상처럼 확정인 자리에 쓴다.</summary>
        public static List<Grant> Certain(string groupId)
            => Merge(Entries(groupId).Select(e => ToGrant(e.Reward)).ToList());

        /// <summary>드랍 묶음 하나를 `roll_count` 만큼 굴린다.</summary>
        public static List<Grant> RollDrop(string dropGroupId, Rng rng)
        {
            var drop = WildlingData.DropTable.FindByDropGroupId(dropGroupId);
            return drop is null
                ? new List<Grant>()
                : Roll(drop.RewardGroupId, rng, drop.RollCount);
        }

        public static Grant ToGrant(Reward reward) => reward switch
        {
            ItemReward item => new Grant
                { Kind = GrantKind.Item, Id = item.ItemId, Amount = item.Amount },
            CurrencyReward currency => new Grant
                { Kind = GrantKind.Currency, Id = currency.CurrencyId, Amount = currency.Amount },
            MonsterReward monster => new Grant
                { Kind = GrantKind.Monster, Id = monster.MonsterId, Amount = monster.Amount },
            ShardReward shard => new Grant
                { Kind = GrantKind.Shard, Id = shard.MonsterId, Amount = shard.Amount },
            _ => new Grant { Kind = GrantKind.Currency, Id = "gold", Amount = 0 },
        };

        /// <summary>같은 것을 하나로 합친다. 여러 번 굴린 결과를 읽기 좋게 만든다.</summary>
        public static List<Grant> Merge(List<Grant> grants)
        {
            var order = new List<(GrantKind, string)>();
            var sums = new Dictionary<(GrantKind, string), int>();

            foreach (var grant in grants)
            {
                var key = (grant.Kind, grant.Id);
                if (!sums.ContainsKey(key))
                {
                    sums[key] = 0;
                    order.Add(key);
                }
                sums[key] += grant.Amount;
            }

            return order.Select(key => new Grant
            {
                Kind = key.Item1,
                Id = key.Item2,
                Amount = sums[key],
            }).ToList();
        }

        /// <summary>지급물 하나를 사람이 읽는 이름으로 만든다.</summary>
        public static string Describe(Grant grant)
        {
            string name = grant.Kind switch
            {
                GrantKind.Item => WildlingData.Item.FindByItemId(grant.Id)?.Name ?? grant.Id,
                GrantKind.Currency =>
                    WildlingData.Currency.FindByCurrencyId(grant.Id)?.Name ?? grant.Id,
                GrantKind.Monster =>
                    WildlingData.Monster.FindByMonsterId(grant.Id)?.Name ?? grant.Id,
                _ => (WildlingData.Monster.FindByMonsterId(grant.Id)?.Name ?? grant.Id) + " 조각",
            };
            return $"{name} ×{grant.Amount}";
        }

        /// <summary>그 지급물의 아이콘 이름이다. 없으면 빈 문자열이다.</summary>
        public static string IconOf(Grant grant) => grant.Kind switch
        {
            GrantKind.Item => WildlingData.Item.FindByItemId(grant.Id)?.Icon ?? "",
            GrantKind.Currency => WildlingData.Currency.FindByCurrencyId(grant.Id)?.Icon ?? "",
            _ => WildlingData.Monster.FindByMonsterId(grant.Id)?.Icon ?? "",
        };
    }
}
