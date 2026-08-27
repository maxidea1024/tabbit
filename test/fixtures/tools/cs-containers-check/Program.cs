// Round-trip check for the generated C# reader of a `set` and a `map`.
//
// Reads the binary the exporter wrote and prints, per row, both layers of the container
// surface: the arrays in the file's order, and what the lookups answer.
//
// **The lookups are the part nothing else can see.** The exported JSON says what the arrays
// hold, so a reader that filled them wrongly is already caught; a dictionary built from the
// wrong column, or not built at all, produces exactly the same JSON. So the probes here ask
// the lookups questions whose answers only come out right if they were built from the keys
// this row holds. spec/types/set-and-map.md section 7.

using System;
using System.Globalization;
using System.Text;
using System.Threading.Tasks;

using Tabbit.Fixtures.Containers;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        if (args.Length < 1)
        {
            Console.Error.WriteLine("usage: cs-containers-check <binary-table-directory>");
            return 2;
        }

        await ContainersAccessor.ReadAllAsync(args[0]);

        var json = new StringBuilder("[");

        for (int i = 0; i < ContainersAccessor.Shop.Records.Count; i++)
        {
            var record = ContainersAccessor.Shop.Records[i];
            var bag = record.Bag;

            if (i > 0)
                json.Append(',');

            json.Append("{\"index\":").Append(record.Index);

            json.Append(",\"tags\":").Append(Strings(bag.Tags));

            // A value the row holds and one it does not, so a set that answered everything
            // and a set that answered nothing both fail.
            json.Append(",\"hasSale\":").Append(bag.ContainsTags("sale") ? "true" : "false");
            json.Append(",\"hasGone\":").Append(bag.ContainsTags("gone") ? "true" : "false");

            json.Append(",\"priceKeys\":").Append(Numbers(bag.Prices.Key));
            json.Append(",\"priceValues\":").Append(Numbers(bag.Prices.Value));
            json.Append(",\"priceCount\":").Append(bag.Prices.Count);

            // Keyed lookup on a map whose value is one column.
            json.Append(",\"priceOf11\":")
                .Append(bag.Prices.TryGetValue(11, out int price) ? price.ToString(CultureInfo.InvariantCulture) : "null");

            json.Append(",\"dropKeys\":").Append(Numbers(bag.Drops.Key));

            // And on one whose value is a struct, where the lookup answers with the entry's
            // position and the members are read at it.
            if (bag.Drops.TryGetIndex(2, out int at))
            {
                json.Append(",\"dropItemAt2\":").Append(bag.Drops.Value.ItemId[at]);
                json.Append(",\"dropCountAt2\":").Append(bag.Drops.Value.Count[at]);
            }
            else
            {
                json.Append(",\"dropItemAt2\":null,\"dropCountAt2\":null");
            }

            json.Append('}');
        }

        json.Append(']');

        Console.Out.Write(json.ToString());
        return 0;
    }

    private static string Numbers(int[] values)
    {
        var sb = new StringBuilder("[");

        for (int i = 0; i < values.Length; i++)
        {
            if (i > 0)
                sb.Append(',');

            sb.Append(values[i].ToString(CultureInfo.InvariantCulture));
        }

        return sb.Append(']').ToString();
    }

    private static string Strings(string[] values)
    {
        var sb = new StringBuilder("[");

        for (int i = 0; i < values.Length; i++)
        {
            if (i > 0)
                sb.Append(',');

            sb.Append('"').Append(values[i]).Append('"');
        }

        return sb.Append(']').ToString();
    }
}
