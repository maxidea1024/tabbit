// Round-trip check for the generated C++ reader of a `set` and a `map`.
//
// Reads the binary the exporter wrote and prints, for one row, both layers of the container
// surface: the vectors in the file's order, and what the lookups beside them answer.
//
// **The lookups are the part nothing else can see.** The exported JSON says what the vectors
// hold, so a reader that filled them wrongly is already caught; a map built from the wrong
// column, or not built at all, produces exactly the same JSON.
//
// spec/types/set-and-map.md section 7.

#include <cstdio>
#include <string>

#include "Tables.h"

int main(int argc, char** argv)
{
    if (argc < 2)
    {
        std::fprintf(stderr, "usage: containers-check <binary-directory>\n");
        return 2;
    }

    containers::Tables tables;

    tables.read_all(argv[1]);

    const auto& first = tables.shop().records()[0].bag;
    const auto& empty = tables.shop().records()[2].bag;

    const std::size_t at = first.drops.index_by_key.at(2);

    std::string tags;
    for (std::size_t i = 0; i < first.tags.size(); ++i)
    {
        if (i > 0) tags += ",";
        tags += "\"" + first.tags[i] + "\"";
    }

    std::string keys;
    for (std::size_t i = 0; i < first.prices.key.size(); ++i)
    {
        if (i > 0) keys += ",";
        keys += std::to_string(first.prices.key[i]);
    }

    std::printf(
        "{\"tags\":[%s],\"hasSale\":%s,\"hasGone\":%s,"
        "\"priceOf11\":%d,"
        "\"dropIndexOf2\":%zu,\"dropItemAt2\":%d,\"dropCountAt2\":%d,"
        "\"priceKeys\":[%s],"
        "\"emptyTagCount\":%zu,\"emptyPriceCount\":%zu}\n",
        tags.c_str(),
        first.tags_set.count("sale") ? "true" : "false",
        first.tags_set.count("gone") ? "true" : "false",
        // A map of scalars answers with the value.
        first.prices.by_key.at(11),
        // A map of structs answers with the entry's position, and the members are read at it.
        at,
        first.drops.value.item_id[at],
        first.drops.value.count[at],
        // The vector keeps the file's order, which the hash map does not.
        keys.c_str(),
        // And a row that wrote nothing has containers of no entries rather than none.
        empty.tags_set.size(),
        empty.prices.by_key.size());

    return 0;
}
