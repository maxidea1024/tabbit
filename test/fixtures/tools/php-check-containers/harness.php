<?php
// Round-trip check for the generated PHP reader of a `set` and a `map`.
//
// Reads the binary the exporter wrote and prints, for one row, both layers of the container
// surface: the lists in the file's order, and what the lookups beside them answer.
//
// **The lookups are the part nothing else can see.** The exported JSON says what the lists
// hold, so a reader that filled them wrongly is already caught; a map built from the wrong
// column, or not built at all, produces exactly the same JSON.
//
// spec/types/set-and-map.md section 7.

declare(strict_types=1);

require_once __DIR__ . '/Tables.php';

if ($argc < 2) {
    fwrite(STDERR, "usage: harness.php <binary-directory>\n");
    exit(2);
}

$tables = new \GameData\Tables();
$tables->readAll($argv[1]);

$first = $tables->shop->records[0]->bag;
$empty = $tables->shop->records[2]->bag;

$at = $first->drops->indexByKey[2];

echo json_encode([
    'tags' => $first->tags,
    'hasSale' => isset($first->tagsSet['sale']),
    'hasGone' => isset($first->tagsSet['gone']),

    // A map of scalars answers with the value.
    'priceOf11' => $first->prices->byKey[11],

    // A map of objects answers with the entry's position, and the properties are read at it.
    'dropIndexOf2' => $at,
    'dropItemAt2' => $first->drops->value->itemId[$at],
    'dropCountAt2' => $first->drops->value->count[$at],

    // Iterating a lookup gives the file's order back - an associative array keeps it.
    'priceKeysInOrder' => array_keys($first->prices->byKey),

    // And a row that wrote nothing has containers of no entries rather than none.
    'emptyTagCount' => count($empty->tagsSet),
    'emptyPriceCount' => count($empty->prices->byKey),
]), PHP_EOL;
