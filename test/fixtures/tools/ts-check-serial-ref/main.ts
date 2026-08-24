// Round-trip check for an array of references - numbered reference columns folded into one.
//
// Read through the accessor rather than a table at a time, because resolution is the
// accessor's: a table read on its own has the keys and nothing to look them up in. That is
// what makes this worth running - the linking pass walks the keys per element, and one bounded
// by the number the sheet happened to have resolves every element but the last.
//
// Both forms of a reference: `slot` is a whole row and `tier` is one of that row's
// values. What is printed is the resolved value, so an element that resolved to the wrong row
// shows as a different word rather than as an equal key.
//
// spec/nullable-array-elements.md · spec/references-in-records.md.
//
// Prints JSON on stdout for the C# harness to assert against.

import { Tables } from './generated/tables'

/** A single disagreement between the read paths. */
interface Mismatch {
    table: string
    index: number
    field: string
    fromJson: string
    fromBinary: string
}

const mismatches: Mismatch[] = []

function compare(table: string, index: number, field: string, fromJson: unknown, fromBinary: unknown): void {
    const a = String(fromJson)
    const b = String(fromBinary)

    if (a !== b) mismatches.push({ table, index, field, fromJson: a, fromBinary: b })
}

/**
 * What a resolved reference points at, or a word saying it points at nothing.
 *
 * The target's name rather than its key, because comparing the key back would pass whatever
 * the linking pass did with it.
 */
function resolved(row: { name: string } | undefined, flag: boolean): string {
    return flag && row ? row.name : '<unresolved>'
}

function main(): number {
    const jsonDir = process.argv[2]
    const binaryDir = process.argv[3]

    if (!jsonDir || !binaryDir) {
        console.error('usage: ts-check-serial-ref <json-dir> <binary-dir>')
        return 2
    }

    const fromJson = new Tables()
    fromJson.readAllSync(jsonDir, '.json')

    const fromBinary = new Tables()
    fromBinary.readAllBinarySync(binaryDir, '.tcb')

    for (let i = 0; i < fromJson.kit.records.length; i++) {
        const j = fromJson.kit.records[i]
        const b = fromBinary.kit.records[i]

        compare('Kit', i, 'index', j.index, b.index)

        // The length the file gave. A read that took it from the generated page instead would
        // agree with itself and disagree with nothing, so it is compared as a value.
        compare('Kit', i, 'slot.length', j.slot.length, b.slot.length)
        compare('Kit', i, 'tier.length', j.tier.length, b.tier.length)

        for (let k = 0; k < b.slot.length; k++) {
            compare('Kit', i, `slot[${k}].key`,
                    j._slot_Piece_index[k], b._slot_Piece_index[k])
            compare('Kit', i, `slot[${k}]`,
                    resolved(j.slot[k], j._slot_F[k]),
                    resolved(b.slot[k], b._slot_F[k]))

            // A field reference resolves to the value itself, so there is no row to name.
            compare('Kit', i, `tier[${k}]`,
                    j._tier_F[k] ? j.tier[k] : '<unresolved>',
                    b._tier_F[k] ? b.tier[k] : '<unresolved>')
        }
    }

    // What it ended up holding, so the two routes agreeing is evidence rather than two copies
    // of one mistake.
    const values = {
        slots: fromBinary.kit.records.map(
            r => r.slot.map((piece, at) => resolved(piece, r._slot_F[at])).join('/')),
        tiers: fromBinary.kit.records.map(
            r => r.tier.map((tier, at) => r._tier_F[at] ? String(tier) : '<unresolved>').join('/')),
    }

    console.log(JSON.stringify(values))
    console.log(JSON.stringify({ mismatches }))

    return mismatches.length === 0 ? 0 : 1
}

process.exit(main())
