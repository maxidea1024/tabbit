# Round-trip check for the generated Ruby reader of a `set` and a `map`.
#
# Reads the binary the exporter wrote and prints, for one row, both layers of the container
# surface: the arrays in the file's order, and what the lookups beside them answer.
#
# **The lookups are the part nothing else can see.** The exported JSON says what the arrays
# hold, so a reader that filled them wrongly is already caught; a map built from the wrong
# column, or not built at all, produces exactly the same JSON.
#
# spec/types/set-and-map.md section 7.

require 'json'

require_relative 'tables'

if ARGV.empty?
  warn 'usage: harness.rb <binary-directory>'
  exit 2
end

tables = GameData::Tables.new
tables.read_all(ARGV[0])

first = tables.shop.records[0].bag
empty = tables.shop.records[2].bag

at = first.drops.index_by_key[2]

puts JSON.generate({
  'tags' => first.tags,
  'hasSale' => first.tags_set.include?('sale'),
  'hasGone' => first.tags_set.include?('gone'),

  # A map of scalars answers with the value.
  'priceOf11' => first.prices.by_key[11],

  # A map of objects answers with the entry's position, and the attributes are read at it.
  'dropIndexOf2' => at,
  'dropItemAt2' => first.drops.value.item_id[at],
  'dropCountAt2' => first.drops.value.count[at],

  # Iterating a lookup gives the file's order back - both of this language's keep it.
  'priceKeysInOrder' => first.prices.by_key.keys,

  # And a row that wrote nothing has containers of no entries rather than none.
  'emptyTagCount' => empty.tags_set.size,
  'emptyPriceCount' => empty.prices.by_key.size,
})
