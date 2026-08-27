/* Round-trip check for the generated C reader of a `set` and a `map`.
 *
 * Reads the binary the exporter wrote and prints, for one row, both layers of the container
 * surface: the arrays in the file's order, and what the lookup functions beside them answer.
 *
 * **The lookups are the part nothing else can see.** The exported JSON says what the arrays
 * hold, so a reader that filled them wrongly is already caught; a lookup that scanned the
 * wrong array produces exactly the same JSON.
 *
 * spec/types/set-and-map.md section 7. */

#include <stdio.h>
#include <stdlib.h>

#include "ContainersData.h"

int main(int argc, char** argv)
{
    ContainersData_t data = {0};
    const struct ContainersData_ShopRecord_t_bag_entry* first;
    const struct ContainersData_ShopRecord_t_bag_entry* empty;
    int32_t at;
    int32_t j;
    char error[256];

    if (argc < 2)
    {
        fprintf(stderr, "usage: containers-check <binary-directory>\n");
        return 2;
    }

    if (!ContainersData_LoadAll(&data, argv[1], error, sizeof error))
    {
        fprintf(stderr, "read failed: %s\n", error);
        return 1;
    }

    first = &data.shop.records[0].bag;
    empty = &data.shop.records[2].bag;

    at = ContainersData_ShopRecord_t_bag_entryDrops_index_of(&first->drops, 2);

    printf("{\"tags\":[");
    for (j = 0; j < first->tags_count; ++j)
    {
        printf("%s\"%s\"", j > 0 ? "," : "", first->tags[j]);
    }

    printf("],\"hasSale\":%s,\"hasGone\":%s",
        ContainersData_ShopRecord_t_bag_entry_contains_tags(first, "sale") ? "true" : "false",
        ContainersData_ShopRecord_t_bag_entry_contains_tags(first, "gone") ? "true" : "false");

    /* A map of scalars: the position, and the value read at it. */
    printf(",\"priceOf11\":%d",
        first->prices.value[
            ContainersData_ShopRecord_t_bag_entryPrices_index_of(&first->prices, 11)]);

    /* A map of structs: the same position, and the members read at it. */
    printf(",\"dropIndexOf2\":%d,\"dropItemAt2\":%d,\"dropCountAt2\":%d",
        at, first->drops.value.item_id[at], first->drops.value.count[at]);

    printf(",\"priceKeys\":[");
    for (j = 0; j < first->prices.key_count; ++j)
    {
        printf("%s%d", j > 0 ? "," : "", first->prices.key[j]);
    }

    /* And a row that wrote nothing has containers of no entries rather than none. */
    printf("],\"emptyTagCount\":%d,\"emptyPriceCount\":%d}\n",
        empty->tags_count, empty->prices.key_count);

    ContainersData_Free(&data);
    return 0;
}
