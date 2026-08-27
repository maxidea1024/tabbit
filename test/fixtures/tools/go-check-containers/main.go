// Round-trip check for the generated Go reader of a `set` and a `map`.
//
// Reads the binary the exporter wrote and prints, for one row, both layers of the container
// surface: the slices in the file's order, and what the maps beside them answer.
//
// **The maps are the part nothing else can see.** The exported JSON says what the slices
// hold, so a reader that filled them wrongly is already caught; a map built from the wrong
// column, or not built at all, produces exactly the same JSON. So the probes here ask
// questions whose answers only come out right if the map was built from this row's keys.
//
// spec/types/set-and-map.md section 7.

package main

import (
	"encoding/json"
	"fmt"
	"os"

	"containers"
)

func main() {
	if len(os.Args) < 2 {
		fmt.Fprintln(os.Stderr, "usage: go-check-containers <binary-directory>")
		os.Exit(2)
	}

	var tables containers.Tables
	if err := tables.ReadAll(os.Args[1]); err != nil {
		fmt.Fprintln(os.Stderr, err)
		os.Exit(1)
	}

	records := tables.Shop.Records()
	first := records[0].Bag
	empty := records[2].Bag

	price, hasPrice := first.Prices.ByKey[11]
	at, hasDrop := first.Drops.IndexByKey[2]

	out := map[string]any{
		"tags":    first.Tags,
		"hasSale": first.TagsSet["sale"],
		"hasGone": first.TagsSet["gone"],

		// A map of scalars answers with the value.
		"priceOf11":    price,
		"priceOf11Set": hasPrice,

		// A map of structs answers with the entry's position, and the fields are read at it.
		"dropIndexOf2":    at,
		"dropIndexOf2Set": hasDrop,
		"dropItemAt2":     first.Drops.Value.ItemId[at],
		"dropCountAt2":    first.Drops.Value.Count[at],

		// The slices keep the file's order, which is the sheet's - a Go map has none.
		"priceKeys": first.Prices.Key,

		// And a row that wrote nothing has containers of no entries rather than none.
		"emptyTagCount":   len(empty.TagsSet),
		"emptyPriceCount": len(empty.Prices.ByKey),
	}

	text, err := json.Marshal(out)
	if err != nil {
		fmt.Fprintln(os.Stderr, err)
		os.Exit(1)
	}

	fmt.Println(string(text))
}
