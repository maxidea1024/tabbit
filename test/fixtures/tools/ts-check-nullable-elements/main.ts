// Round-trip check for an array whose elements may have no value.
//
// The table read from the binary export, compared against the JSON the exporter wrote from
// the same sheet. The two carry an absent element completely differently - the JSON writes
// `null` in the array, the binary writes a bit in a bitmap in front of the values - so
// agreeing is evidence rather than coincidence.
//
// Read from the file rather than through the generated JSON path: what is being checked is
// the bitmap and the counter that walks it, and going through one reader on both sides
// would let a wrong walk agree with itself.
//
// `words` is a `string?[]` and it is in the comparison on purpose: an absent element and an
// empty string are the same value, so only the bit tells them apart.
//
// spec/types/nullable-array-elements.md.

import * as fs from 'fs'

import { ListingTable } from './generated/tables/listing'

interface Mismatch {
    index: number
    field: string
    fromJson: string
    fromBinary: string
}

const mismatches: Mismatch[] = []

function render(value: unknown): string {
    if (Array.isArray(value)) return '[' + value.map(render).join(',') + ']'
    if (value === null || value === undefined) return 'null'
    return String(value)
}

function compare(index: number, field: string, fromJson: unknown, fromBinary: unknown): void {
    const a = render(fromJson)
    const b = render(fromBinary)

    if (a !== b) mismatches.push({ index, field, fromJson: a, fromBinary: b })
}

function main(): number {
    const jsonDir = process.argv[2]
    const binaryDir = process.argv[3]

    if (!jsonDir || !binaryDir) {
        console.error('usage: ts-check-nullable-elements <json-dir> <binary-dir>')
        return 2
    }

    const rows: any[] = JSON.parse(fs.readFileSync(`${jsonDir}/Listing.json`, 'utf8'))

    const table = new ListingTable()
    table.readBinarySync(`${binaryDir}/Listing.tcb`)

    compare(-1, 'recordCount', rows.length, table.records.length)

    for (let i = 0; i < rows.length; i++) {
        const json = rows[i]
        const record = table.records[i]

        compare(i, 'index', json.index, record.index)

        // The array with the marker inside the brackets: every element, with an absent one
        // rendered the way the JSON renders it.
        compare(i, 'holes', json.holes,
            record.holes.map((value, at) => record.hasHolesAt(at) ? value : null))

        // And the one with the marker on both sides: the array itself may be gone.
        compare(i, 'both', json.both,
            record.hasBoth
                ? record.both.map((value, at) => record.hasBothAt(at) ? value : null)
                : null)

        compare(i, 'words', json.words,
            record.words.map((value, at) => record.hasWordsAt(at) ? value : null))
    }

    console.log(JSON.stringify({
        holesFromBinary: table.records.map(r => r.holes.map((_, at) => r.hasHolesAt(at))),
        wordsFromBinary: table.records.map(r => r.words.map((_, at) => r.hasWordsAt(at))),
    }))

    console.log(JSON.stringify({ mismatches }))

    return mismatches.length === 0 ? 0 : 1
}

process.exit(main())
