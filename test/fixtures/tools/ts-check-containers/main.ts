// Round-trip check for a `set` and a `map` in TypeScript.
//
// The same table read from the JSON export and from the binary export. Both layers of the
// surface are compared: the arrays, which the file's order settles, and the `Set` and `Map`
// beside them, which neither export carries - they are built where the rows are published,
// and that is the one place both read paths meet.
//
// **The lookups are why the JSON path matters here.** TypeScript is the only language that
// reads the .json, and a lookup built only on the binary path would leave a project reading
// the export with an empty `Map` and no error - which is exactly the kind of thing two paths
// side by side catch and neither alone does. spec/types/set-and-map.md sections 7.3 and 8.
//
// Prints JSON on stdout for the C# harness to assert against.

import * as fs from 'fs'

import { ShopTable } from './generated/tables/shop'

/** A single disagreement between the two read paths. */
interface Mismatch {
    table: string
    index: number
    field: string
    fromJson: string
    fromBinary: string
}

const mismatches: Mismatch[] = []

/** Renders a value for comparison, including the containers. */
function render(value: unknown): string {
    if (Array.isArray(value)) return '[' + value.map(render).join(',') + ']'
    if (value instanceof Map) {
        return 'map{' + Array.from(value.entries())
            .map(([k, v]) => `${render(k)}:${render(v)}`).join(',') + '}'
    }
    if (value instanceof Set) return 'set{' + Array.from(value).map(render).join(',') + '}'
    if (typeof value === 'bigint') return value.toString()
    if (value === null || value === undefined) return String(value)
    if (typeof value === 'object') {
        const entries = Object.keys(value as object).sort()
            .map(k => `${k}:${render((value as any)[k])}`)
        return '{' + entries.join(',') + '}'
    }
    return String(value)
}

function compare(table: string, index: number, field: string, fromJson: unknown, fromBinary: unknown): void {
    const a = render(fromJson)
    const b = render(fromBinary)

    if (a !== b) mismatches.push({ table, index, field, fromJson: a, fromBinary: b })
}

function main(): number {
    const jsonDir = process.argv[2]
    const binaryDir = process.argv[3]

    if (!jsonDir || !binaryDir) {
        console.error('usage: ts-check-containers <json-dir> <binary-dir>')
        return 2
    }

    const fromJson = new ShopTable()
    fromJson.readJsonFrom(fs.readFileSync(`${jsonDir}/Shop.json`, 'utf8'))

    const fromBinary = new ShopTable()
    fromBinary.readBinaryFrom(new Uint8Array(fs.readFileSync(`${binaryDir}/Shop.tcb`)))

    compare('Shop', -1, 'recordCount', fromJson.records.length, fromBinary.records.length)

    for (let i = 0; i < fromJson.records.length; i++) {
        const j = fromJson.records[i].bag
        const b = fromBinary.records[i].bag

        // The ordered layer, which the file settles.
        compare('Shop', i, 'bag.tags', j.tags, b.tags)
        compare('Shop', i, 'bag.prices.key', j.prices.key, b.prices.key)
        compare('Shop', i, 'bag.prices.value', j.prices.value, b.prices.value)
        compare('Shop', i, 'bag.drops.key', j.drops.key, b.drops.key)
        compare('Shop', i, 'bag.drops.value.itemId', j.drops.value.itemId, b.drops.value.itemId)
        compare('Shop', i, 'bag.drops.value.count', j.drops.value.count, b.drops.value.count)

        // The lookup layer, which neither export carries. Compared by content and by order,
        // because `Map` and `Set` keep insertion order here and the insertion order is the
        // file's - so a path that built one from the wrong array is caught either way.
        compare('Shop', i, 'bag.tagsSet', j.tagsSet, b.tagsSet)
        compare('Shop', i, 'bag.prices.byKey', j.prices.byKey, b.prices.byKey)
        compare('Shop', i, 'bag.drops.indexByKey', j.drops.indexByKey, b.drops.indexByKey)
    }

    // What the containers actually answer, so the harness can assert on values rather than
    // only on the two paths agreeing - they could agree and both be wrong.
    const first = fromBinary.records[0].bag
    const empty = fromBinary.records[2].bag

    console.log(JSON.stringify({
        tags: Array.from(first.tagsSet),
        hasSale: first.tagsSet.has('sale'),
        hasGone: first.tagsSet.has('gone'),

        // A map of scalars answers with the value.
        priceOf11: first.prices.byKey.get(11) ?? null,

        // A map of structs answers with the entry's position, and the members are read at it.
        dropIndexOf2: first.drops.indexByKey.get(2) ?? null,
        dropItemAt2: first.drops.value.itemId[first.drops.indexByKey.get(2) as number] ?? null,

        // Iterating a lookup gives the file's order back, which is what `Map` keeping
        // insertion order buys.
        priceKeysInOrder: Array.from(first.prices.byKey.keys()),

        // And a row that holds nothing has containers of no entries rather than none.
        emptyTagCount: empty.tagsSet.size,
        emptyPriceCount: empty.prices.byKey.size,
    }))

    console.log(JSON.stringify({ mismatches }))

    return mismatches.length === 0 ? 0 : 1
}

process.exit(main())
