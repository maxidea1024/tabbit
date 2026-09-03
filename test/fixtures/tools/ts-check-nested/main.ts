// Round-trip check for record groups in the TypeScript reader.
//
// The same table read from the JSON export and from the binary export, compared field
// by field. For records that is the check worth having: the JSON carries a record as an
// object and the binary carries it as one fixed-array column per member, so the two
// paths reach the same values by genuinely different routes. If the nesting notation,
// the JSON shape and the wire layout do not agree, this is where it shows.
//
// The `core` driver next door does the same for everything that is not a record; the two
// are separate because each names the tables of its own fixture.
//
// Prints JSON on stdout for the C# harness to assert against.

import * as fs from 'fs'

import { LoadoutTable } from './generated/tables/loadout'

/** A single disagreement between the two read paths. */
interface Mismatch {
    table: string
    index: number
    field: string
    fromJson: string
    fromBinary: string
}

const mismatches: Mismatch[] = []

/**
 * Renders a value for comparison.
 *
 * Everything goes to text so an array compares by its elements and a record by its
 * members, whichever route produced them.
 */
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
        console.error('usage: ts-check-nested <json-dir> <binary-dir>')
        return 2
    }

    const fromJson = new LoadoutTable()
    fromJson.readJsonFrom(fs.readFileSync(`${jsonDir}/Loadout.json`, 'utf8'))

    const fromBinary = new LoadoutTable()
    fromBinary.readBinaryFrom(new Uint8Array(fs.readFileSync(`${binaryDir}/Loadout.tcb`)))

    compare('Loadout', -1, 'recordCount', fromJson.records.length, fromBinary.records.length)

    for (let i = 0; i < fromJson.records.length; i++) {
        const j = fromJson.records[i]
        const b = fromBinary.records[i]

        // The plain columns, so a failure in them is not read as a record problem.
        compare('Loadout', i, 'index', j.index, b.index)
        compare('Loadout', i, 'name', j.name, b.name)
        compare('Loadout', i, 'note', j.note, b.note)

        // A record with no serial number: one object, not an array of one.
        compare('Loadout', i, 'pos', j.pos, b.pos)
        compare('Loadout', i, 'pos.x', j.pos.x, b.pos.x)
        compare('Loadout', i, 'pos.y', j.pos.y, b.pos.y)

        // An array of records whose members are of different types, which is the case an
        // array of scalars cannot express.
        compare('Loadout', i, 'slot.length', j.slot.length, b.slot.length)
        compare('Loadout', i, 'slot', j.slot, b.slot)

        for (let k = 0; k < j.slot.length; k++) {
            compare('Loadout', i, `slot[${k}].id`, j.slot[k].id, b.slot[k].id)
            compare('Loadout', i, `slot[${k}].label`, j.slot[k].label, b.slot[k].label)
        }

        // The scalar serial field beside them, which the notation must not have changed.
        compare('Loadout', i, 'tag', j.tag, b.tag)
    }

    // What the records actually hold, so the harness can assert on values rather than
    // only on the two paths agreeing with each other - they could agree and both be wrong.
    console.log(JSON.stringify({
        slotFromBinary: fromBinary.records.map(r => r.slot.map(s => `${s.id}:${s.label}`)),
        slotFromJson: fromJson.records.map(r => r.slot.map(s => `${s.id}:${s.label}`)),
        posFromBinary: fromBinary.records.map(r => `${r.pos.x},${r.pos.y}`),
        posFromJson: fromJson.records.map(r => `${r.pos.x},${r.pos.y}`),
    }))

    console.log(JSON.stringify({ mismatches }))

    return mismatches.length === 0 ? 0 : 1
}

process.exit(main())
