# Round-trip check for the generated Python reader of a `set` and a `map`.
#
# Reads the binary the exporter wrote and prints, for one row, both layers of the container
# surface: the lists in the file's order, and what the lookups beside them answer.
#
# **The lookups are the part nothing else can see.** The exported JSON says what the lists
# hold, so a reader that filled them wrongly is already caught; a map built from the wrong
# column, or not built at all, produces exactly the same JSON.
#
# spec/types/set-and-map.md section 7.

import json
import sys

from gamedata import Tables


def main():
    if len(sys.argv) < 2:
        print("usage: harness.py <binary-directory>", file=sys.stderr)
        return 2

    tables = Tables()
    tables.read_all(sys.argv[1])

    first = tables.shop.records[0].bag
    empty = tables.shop.records[2].bag

    at = first.drops.index_by_key[2]

    print(json.dumps({
        "tags": list(first.tags),
        "hasSale": "sale" in first.tags_set,
        "hasGone": "gone" in first.tags_set,

        # A map of scalars answers with the value.
        "priceOf11": first.prices.by_key[11],

        # A map of objects answers with the entry's position, and the attributes are read
        # at it.
        "dropIndexOf2": at,
        "dropItemAt2": first.drops.value.item_id[at],
        "dropCountAt2": first.drops.value.count[at],

        # Iterating a `dict` gives the file's order back; a `set` has none, which is what
        # the list beside it is for.
        "priceKeysInOrder": list(first.prices.by_key.keys()),

        # And a row that wrote nothing has containers of no entries rather than none.
        "emptyTagCount": len(empty.tags_set),
        "emptyPriceCount": len(empty.prices.by_key),
    }))

    return 0


sys.exit(main())
