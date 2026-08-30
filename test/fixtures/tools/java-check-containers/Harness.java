// Round-trip check for the generated Java reader of a `set` and a `map`.
//
// Reads the binary the exporter wrote and prints, for one row, both layers of the container
// surface: the arrays in the file's order, and what the lookups beside them answer.
//
// **The lookups are the part nothing else can see.** The exported JSON says what the arrays
// hold, so a reader that filled them wrongly is already caught; a map built from the wrong
// column, or not built at all, produces exactly the same JSON.
//
// spec/types/set-and-map.md section 7.

import java.util.StringJoiner;

import containers.ContainersData;
import containers.Bag;

public final class Harness {
    public static void main(String[] args) throws Exception {
        if (args.length < 1) {
            System.err.println("usage: Harness <binary-directory>");
            System.exit(2);
        }

        ContainersData data = new ContainersData();
        data.readAll(args[0]);

        Bag first = data.shop.records().get(0).bag;
        Bag empty = data.shop.records().get(2).bag;

        Integer at = first.drops.indexByKey.get(2);

        StringJoiner out = new StringJoiner(",", "{", "}");
        out.add("\"tags\":" + strings(first.tags));
        out.add("\"hasSale\":" + first.tagsSet.contains("sale"));
        out.add("\"hasGone\":" + first.tagsSet.contains("gone"));

        // A map of scalars answers with the value.
        out.add("\"priceOf11\":" + first.prices.byKey.get(11));

        // A map of structs answers with the entry's position, and the fields are read at it.
        out.add("\"dropIndexOf2\":" + at);
        out.add("\"dropItemAt2\":" + first.drops.value.itemId[at]);
        out.add("\"dropCountAt2\":" + first.drops.value.count[at]);

        // Iterating a lookup gives the file's order back, which `LinkedHashMap` is for.
        out.add("\"priceKeysInOrder\":" + first.prices.byKey.keySet().toString());

        // And a row that wrote nothing has containers of no entries rather than none.
        out.add("\"emptyTagCount\":" + empty.tagsSet.size());
        out.add("\"emptyPriceCount\":" + empty.prices.byKey.size());

        System.out.println(out);
    }

    private static String strings(String[] values) {
        StringJoiner joiner = new StringJoiner(",", "[", "]");

        for (String value : values)
            joiner.add("\"" + value + "\"");

        return joiner.toString();
    }
}
