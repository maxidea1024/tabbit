// Round-trip check for the generated Rust reader of a `set` and a `map`.
//
// Reads the binary the exporter wrote and prints, for one row, both layers of the container
// surface: the vectors in the file's order, and what the lookups beside them answer.
//
// **The lookups are the part nothing else can see.** The exported JSON says what the vectors
// hold, so a reader that filled them wrongly is already caught; a map built from the wrong
// column, or not built at all, produces exactly the same JSON.
//
// spec/types/set-and-map.md section 7.

use std::path::Path;

use containers::Tables;

fn main() {
    let arguments: Vec<String> = std::env::args().collect();

    if arguments.len() < 2 {
        eprintln!("usage: harness <binary-directory>");
        std::process::exit(2);
    }

    let mut tables = Tables::default();
    tables.read_all(Path::new(&arguments[1])).expect("read");

    let first = &tables.shop.records()[0].bag;
    let empty = &tables.shop.records()[2].bag;

    let at = first.drops.index_by_key[&2];

    let tags: Vec<String> = first.tags.iter().map(|t: &String| format!("\"{}\"", t)).collect();
    let keys: Vec<String> = first.prices.key.iter().map(|k: &i32| k.to_string()).collect();

    println!(
        concat!(
            "{{\"tags\":[{}],\"hasSale\":{},\"hasGone\":{},",
            "\"priceOf11\":{},",
            "\"dropIndexOf2\":{},\"dropItemAt2\":{},\"dropCountAt2\":{},",
            "\"priceKeys\":[{}],",
            "\"emptyTagCount\":{},\"emptyPriceCount\":{}}}"
        ),
        tags.join(","),
        first.tags_set.contains("sale"),
        first.tags_set.contains("gone"),
        // A map of scalars answers with the value.
        first.prices.by_key[&11],
        // A map of structs answers with the entry's position, and the fields are read at it.
        at,
        first.drops.value.item_id[at],
        first.drops.value.count[at],
        // The vector keeps the file's order, which the hash map does not.
        keys.join(","),
        // And a row that wrote nothing has containers of no entries rather than none.
        empty.tags_set.len(),
        empty.prices.by_key.len(),
    );
}
