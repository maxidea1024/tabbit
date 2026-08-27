// Round-trip check for the generated Kotlin reader of a `set` and a `map`.
//
// Reads the binary the exporter wrote and prints, for one row, both layers of the container
// surface: the lists in the file's order, and what the lookups beside them answer.
//
// **The lookups are the part nothing else can see.** The exported JSON says what the lists
// hold, so a reader that filled them wrongly is already caught; a map built from the wrong
// column, or not built at all, produces exactly the same JSON.
//
// spec/types/set-and-map.md section 7.

import containers.ContainersData

fun main(args: Array<String>) {
    if (args.isEmpty()) {
        System.err.println("usage: Harness <binary-directory>")
        kotlin.system.exitProcess(2)
    }

    ContainersData.readAll(args[0])

    val first = ContainersData.shop.records[0].bag
    val empty = ContainersData.shop.records[2].bag

    val at = first.drops.indexByKey[2]!!

    val parts = listOf(
        "\"tags\":[" + first.tags.joinToString(",") { "\"" + it + "\"" } + "]",
        "\"hasSale\":" + first.tagsSet.contains("sale"),
        "\"hasGone\":" + first.tagsSet.contains("gone"),

        // A map of scalars answers with the value.
        "\"priceOf11\":" + first.prices.byKey[11],

        // A map of structs answers with the entry's position, and the properties are read
        // at it.
        "\"dropIndexOf2\":" + at,
        "\"dropItemAt2\":" + first.drops.value.itemId[at],
        "\"dropCountAt2\":" + first.drops.value.count[at],

        // Iterating a lookup gives the file's order back, which `LinkedHashMap` is for.
        "\"priceKeysInOrder\":[" + first.prices.byKey.keys.joinToString(",") + "]",

        // And a row that wrote nothing has containers of no entries rather than none.
        "\"emptyTagCount\":" + empty.tagsSet.size,
        "\"emptyPriceCount\":" + empty.prices.byKey.size,
    )

    println("{" + parts.joinToString(",") + "}")
}
