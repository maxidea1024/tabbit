// Round-trip check for a record whose member is itself a record.
//
// The same table read from the JSON export and from the binary export. The two routes are
// genuinely different at this depth: the JSON carries the nested object whole, while the
// binary carries one fixed-array column per **leaf** - `Deep.Star.Position.X` - and the
// reader rebuilds the nesting from the member path. Agreeing is evidence rather than
// coincidence.
//
// The compact JSON is the third route and the one most likely to be wrong, because it is
// positional over the wire columns: reading one entry per member rather than per leaf would
// take the first leaf's run and call it the whole record.
//
// spec/nested-multi-level.md.
//
// Prints JSON on stdout for the C# harness to assert against.

import { DeepTable } from './generated/tables/deep'

/** A single disagreement between the read paths. */
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
 * Everything goes to text so an array compares by its elements and a record by its members,
 * whichever route produced them - and however deep they go.
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
        console.error('usage: ts-check-nested-deep <json-dir> <binary-dir>')
        return 2
    }

    const fromJson = new DeepTable()
    fromJson.readSync(`${jsonDir}/Deep.json`)

    const fromBinary = new DeepTable()
    fromBinary.readBinarySync(`${binaryDir}/Deep.tcb`)

    // The compact row, read through the same class - a third route over the same cells.
    const fromCompact = new DeepTable()
    fromCompact.readSync(`${jsonDir}/../json-compact/Deep.json`)

    compare('Deep', -1, 'recordCount', fromJson.records.length, fromBinary.records.length)

    for (let i = 0; i < fromJson.records.length; i++) {
        const j = fromJson.records[i]
        const b = fromBinary.records[i]
        const c = fromCompact.records[i]

        // The plain column, so a failure in it is not read as a nesting problem.
        compare('Deep', i, 'index', j.index, b.index)

        // The shape under test, whole and then level by level - so a disagreement names the
        // level it happened at rather than printing two object dumps.
        compare('Deep', i, 'star', j.star, b.star)
        compare('Deep', i, 'star.length', j.star.length, b.star.length)

        for (let k = 0; k < j.star.length; k++) {
            compare('Deep', i, `star[${k}].id`, j.star[k].id, b.star[k].id)
            compare('Deep', i, `star[${k}].position`, j.star[k].position, b.star[k].position)
            compare('Deep', i, `star[${k}].position.x`, j.star[k].position.x, b.star[k].position.x)
            compare('Deep', i, `star[${k}].position.y`, j.star[k].position.y, b.star[k].position.y)

            // And the compact route against the binary, which is what catches a slice taken
            // per member instead of per leaf.
            compare('Deep', i, `compact star[${k}].id`, c.star[k].id, b.star[k].id)
            compare('Deep', i, `compact star[${k}].position.x`, c.star[k].position.x, b.star[k].position.x)
            compare('Deep', i, `compact star[${k}].position.y`, c.star[k].position.y, b.star[k].position.y)
        }
    }

    // What the records actually hold, so the harness can assert on values rather than only on
    // the routes agreeing - they could agree and all be wrong.
    const render1 = (t: DeepTable) =>
        t.records.map(r => r.star.map(s => `${s.id}:${s.position.x},${s.position.y}`).join('/'))

    console.log(JSON.stringify({
        starFromBinary: render1(fromBinary),
        starFromJson: render1(fromJson),
        starFromCompact: render1(fromCompact),
    }))

    console.log(JSON.stringify({ mismatches }))

    return mismatches.length === 0 ? 0 : 1
}

process.exit(main())
