// Round-trip check for trimmed record arrays in the TypeScript reader.
//
// The same table read from the JSON export and from the binary export, compared row by
// row. A variable length reaches the two by different routes: the JSON simply carries a
// shorter array, while the binary writes a count per row per member and the compact JSON
// gives each member one nested entry. Three encodings of one fact, so agreeing is
// evidence rather than coincidence.
//
// The `ts-check-nested` driver next door does the fixed-length case. They are separate
// because each names the tables of its own fixture.
//
// Prints JSON on stdout for the C# harness to assert against.

import * as fs from 'fs'

import { LootTable } from './generated/tables/loot'

/** A single disagreement between the two read paths. */
interface Mismatch {
    table: string
    index: number
    field: string
    fromJson: string
    fromBinary: string
}

const mismatches: Mismatch[] = []

function render(value: unknown): string {
    if (Array.isArray(value)) return '[' + value.map(render).join(',') + ']'
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
        console.error('usage: ts-check-record-trim <json-dir> <binary-dir>')
        return 2
    }

    const fromJson = new LootTable()
    fromJson.readJsonFrom(fs.readFileSync(`${jsonDir}/Loot.json`, 'utf8'))

    const fromBinary = new LootTable()
    fromBinary.readBinaryFrom(new Uint8Array(fs.readFileSync(`${binaryDir}/Loot.tcb`)))

    compare('Loot', -1, 'recordCount', fromJson.records.length, fromBinary.records.length)

    for (let i = 0; i < fromJson.records.length; i++) {
        const j = fromJson.records[i]
        const b = fromBinary.records[i]

        // The plain columns, so a failure in them is not read as a trimming problem.
        compare('Loot', i, 'index', j.index, b.index)
        compare('Loot', i, 'name', j.name, b.name)

        // The length first: it is the thing under test, and comparing it separately says
        // which of the two paths disagreed rather than only that they did.
        compare('Loot', i, 'slot.length', j.slot.length, b.slot.length)
        compare('Loot', i, 'slot', j.slot, b.slot)

        for (let k = 0; k < j.slot.length; k++) {
            compare('Loot', i, `slot[${k}].id`, j.slot[k].id, b.slot[k].id)
            compare('Loot', i, `slot[${k}].count`, j.slot[k].count, b.slot[k].count)
        }

        // A record that is not an array, in a table that trims: nothing to trim, so it
        // stays one object.
        compare('Loot', i, 'pos', j.pos, b.pos)
    }

    // What the rows actually hold, so the harness can assert on lengths and values rather
    // than only on the two paths agreeing - they could agree and both be wrong.
    console.log(JSON.stringify({
        lengthsFromBinary: fromBinary.records.map(r => r.slot.length),
        lengthsFromJson: fromJson.records.map(r => r.slot.length),
        slotFromBinary: fromBinary.records.map(r => r.slot.map(s => `${s.id}:${s.count}`)),
        slotFromJson: fromJson.records.map(r => r.slot.map(s => `${s.id}:${s.count}`)),
    }))

    console.log(JSON.stringify({ mismatches }))

    return mismatches.length === 0 ? 0 : 1
}

process.exit(main())
