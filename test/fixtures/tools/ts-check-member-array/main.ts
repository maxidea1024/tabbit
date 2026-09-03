// Round-trip check for a record whose members are arrays.
//
// The same table read from the JSON export and from the binary export. This shape shares
// its columns and its wire with an array of records and differs only in what they are
// assembled into - `{ m: [a, b] }` rather than `[{ m: a }, { m: b }]` - so the check that
// matters is that both read paths build the same one. The JSON carries the object; the
// binary carries one fixed-array column per member and the reader indexes the member.
//
// spec/types/nested-multi-level.md. The `ts-check-nested` driver next door does the same for the
// array of records this is the mirror of.
//
// Prints JSON on stdout for the C# harness to assert against.

import * as fs from 'fs'

import { GuideTable } from './generated/tables/guide'
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
        console.error('usage: ts-check-member-array <json-dir> <binary-dir>')
        return 2
    }

    const fromJson = new GuideTable()
    fromJson.readJsonFrom(fs.readFileSync(`${jsonDir}/Guide.json`, 'utf8'))

    const fromBinary = new GuideTable()
    fromBinary.readBinaryFrom(new Uint8Array(fs.readFileSync(`${binaryDir}/Guide.tcb`)))

    compare('Guide', -1, 'recordCount', fromJson.records.length, fromBinary.records.length)

    for (let i = 0; i < fromJson.records.length; i++) {
        const j = fromJson.records[i]
        const b = fromBinary.records[i]

        // The plain columns, so a failure in them is not read as a record problem.
        compare('Guide', i, 'index', j.index, b.index)
        compare('Guide', i, 'name', j.name, b.name)

        // The shape under test: one record, and every member holding all of its elements.
        compare('Guide', i, 'skill', j.skill, b.skill)
        compare('Guide', i, 'skill.step', j.skill.step, b.skill.step)
        compare('Guide', i, 'skill.order', j.skill.order, b.skill.order)
        compare('Guide', i, 'skill.step.length', j.skill.step.length, b.skill.step.length)

        // A record with no number at all, beside it - still one record with scalar members,
        // which is what says the two shapes did not get confused for each other.
        compare('Guide', i, 'pos', j.pos, b.pos)

        // And the scalar serial field, which the notation must not have changed.
        compare('Guide', i, 'tag', j.tag, b.tag)

        // The array of arrays: same columns and same wire as the record above, assembled
        // with the outer level indexed rather than named. spec/types/nested-multi-level.md.
        compare('Guide', i, 'grid', j.grid, b.grid)
        compare('Guide', i, 'grid.length', j.grid.length, b.grid.length)
        compare('Guide', i, 'grid[0].length', j.grid[0].length, b.grid[0].length)
    }

    // What the records actually hold, so the harness can assert on values rather than only
    // on the two paths agreeing - they could agree and both be wrong.
    console.log(JSON.stringify({
        skillFromBinary: fromBinary.records.map(r => `${r.skill.step.join('|')}/${r.skill.order.join('|')}`),
        skillFromJson: fromJson.records.map(r => `${r.skill.step.join('|')}/${r.skill.order.join('|')}`),
        gridFromBinary: fromBinary.records.map(r => r.grid.map(inner => inner.join('|')).join('/')),
        gridFromJson: fromJson.records.map(r => r.grid.map(inner => inner.join('|')).join('/')),
        posFromBinary: fromBinary.records.map(r => `${r.pos.x},${r.pos.y}`),
        posFromJson: fromJson.records.map(r => `${r.pos.x},${r.pos.y}`),
    }))

    console.log(JSON.stringify({ mismatches }))

    return mismatches.length === 0 ? 0 : 1
}

process.exit(main())
