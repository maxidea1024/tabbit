// Round-trip check for the generated Swift reader of a `set` and a `map`.
//
// Reads the binary the exporter wrote and prints, for one row, both layers of the container
// surface: the arrays in the file's order, and what the lookups beside them answer.
//
// **The lookups are the part nothing else can see.** The exported JSON says what the arrays
// hold, so a reader that filled them wrongly is already caught; a map built from the wrong
// column, or not built at all, produces exactly the same JSON.
//
// spec/types/set-and-map.md section 7.

import Foundation

let arguments = CommandLine.arguments

if arguments.count < 2 {
    FileHandle.standardError.write("usage: main <binary-directory>\n".data(using: .utf8)!)
    exit(2)
}

let tables = Tables()
try tables.readAll(arguments[1])

let first = tables.shop.records[0].bag
let empty = tables.shop.records[2].bag

let at = first.drops.indexByKey[2]!

// Written by hand rather than through JSONEncoder: the keys have to come out in a fixed
// order for the harness to read, and this is one object.
var parts: [String] = []
parts.append("\"tags\":[" + first.tags.map { "\"\($0)\"" }.joined(separator: ",") + "]")
parts.append("\"hasSale\":\(first.tagsSet.contains("sale"))")
parts.append("\"hasGone\":\(first.tagsSet.contains("gone"))")

// A map of scalars answers with the value.
parts.append("\"priceOf11\":\(first.prices.byKey[11]!)")

// A map of structs answers with the entry's position, and the properties are read at it.
parts.append("\"dropIndexOf2\":\(at)")
parts.append("\"dropItemAt2\":\(first.drops.value.itemId[at])")
parts.append("\"dropCountAt2\":\(first.drops.value.count[at])")

// The array keeps the file's order, which the dictionary does not.
parts.append("\"priceKeys\":[" + first.prices.key.map { "\($0)" }.joined(separator: ",") + "]")

// And a row that wrote nothing has containers of no entries rather than none.
parts.append("\"emptyTagCount\":\(empty.tagsSet.count)")
parts.append("\"emptyPriceCount\":\(empty.prices.byKey.count)")

print("{" + parts.joined(separator: ",") + "}")
