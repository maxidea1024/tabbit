// Round-trip check for the generated Dart reader of a `set` and a `map`.
//
// Reads the binary the exporter wrote and prints, for one row, both layers of the container
// surface: the lists in the file's order, and what the lookups beside them answer.
//
// **The lookups are the part nothing else can see.** The exported JSON says what the lists
// hold, so a reader that filled them wrongly is already caught; a map built from the wrong
// column, or not built at all, produces exactly the same JSON.
//
// spec/types/set-and-map.md section 7.

import 'dart:convert';
import 'dart:io';

import 'tables.dart';

void main(List<String> args) {
  if (args.isEmpty) {
    stderr.writeln('usage: harness.dart <binary-directory>');
    exit(2);
  }

  final tables = Tables();
  tables.readAll(args[0]);

  final first = tables.shop.records[0].bag;
  final empty = tables.shop.records[2].bag;

  final at = first.drops.indexByKey[2]!;

  stdout.writeln(jsonEncode(<String, dynamic>{
    'tags': first.tags,
    'hasSale': first.tagsSet.contains('sale'),
    'hasGone': first.tagsSet.contains('gone'),

    // A map of scalars answers with the value.
    'priceOf11': first.prices.byKey[11],

    // A map of structs answers with the entry's position, and the fields are read at it.
    'dropIndexOf2': at,
    'dropItemAt2': first.drops.value.itemId[at],
    'dropCountAt2': first.drops.value.count[at],

    // Iterating a lookup gives the file's order back, which Dart's default `Map` is.
    'priceKeysInOrder': first.prices.byKey.keys.toList(),

    // And a row that wrote nothing has containers of no entries rather than none.
    'emptyTagCount': empty.tagsSet.length,
    'emptyPriceCount': empty.prices.byKey.length,
  }));
}
