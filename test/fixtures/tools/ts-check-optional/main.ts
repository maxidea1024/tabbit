// Round-trip check for optional columns in the TypeScript reader.
//
// The same table read from the JSON export and from the binary export, compared value by
// value **and presence by presence**. The two carry absence completely differently - the
// JSON writes `null`, the binary writes a bit in a bitmap at the front of the column's
// block - so agreeing is evidence rather than coincidence.
//
// Prints JSON on stdout for the C# harness to assert against.

import * as fs from 'fs'

import { DropTable } from './generated/tables/drop'

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

function compare(index: number, field: string, fromJson: unknown, fromBinary: unknown): void {
    const a = render(fromJson)
    const b = render(fromBinary)

    if (a !== b) mismatches.push({ table: 'Drop', index, field, fromJson: a, fromBinary: b })
}

function main(): number {
    const jsonDir = process.argv[2]
    const binaryDir = process.argv[3]

    if (!jsonDir || !binaryDir) {
        console.error('usage: ts-check-optional <json-dir> <binary-dir>')
        return 2
    }

    const fromJson = new DropTable()
    fromJson.readJsonFrom(fs.readFileSync(`${jsonDir}/Drop.json`, 'utf8'))

    const fromBinary = new DropTable()
    fromBinary.readBinaryFrom(new Uint8Array(fs.readFileSync(`${binaryDir}/Drop.tcb`)))

    compare(-1, 'recordCount', fromJson.records.length, fromBinary.records.length)

    for (let i = 0; i < fromJson.records.length; i++) {
        const j = fromJson.records[i]
        const b = fromBinary.records[i]

        // The required columns first, so a failure in them is not read as a presence
        // problem.
        compare(i, 'index', j.index, b.index)
        compare(i, 'hp', j.hp, b.hp)

        // Presence, then the value. Both, because the value of an absent row is the type's
        // empty one on either path and comparing only that would pass even if presence
        // disagreed.
        compare(i, 'hasBonus', j.hasBonus, b.hasBonus)
        compare(i, 'bonus', j.bonus, b.bonus)
        compare(i, 'hasWeight', j.hasWeight, b.hasWeight)
        compare(i, 'weight', j.weight, b.weight)
        compare(i, 'hasCount', j.hasCount, b.hasCount)
        compare(i, 'count', j.count, b.count)
        compare(i, 'hasOpenAt', j.hasOpenAt, b.hasOpenAt)
        compare(i, 'openAt', j.openAt, b.openAt)
        compare(i, 'hasGrade', j.hasGrade, b.hasGrade)
        compare(i, 'grade', j.grade, b.grade)
        compare(i, 'hasCosts', j.hasCosts, b.hasCosts)
        compare(i, 'costs', j.costs, b.costs)
        compare(i, 'hasLabel', j.hasLabel, b.hasLabel)
        compare(i, 'label', j.label, b.label)
        compare(i, 'hasHidden', j.hasHidden, b.hasHidden)
        compare(i, 'hidden', j.hidden, b.hidden)
    }

    console.log(JSON.stringify({
        presenceFromBinary: fromBinary.records.map(r => [r.hasBonus, r.hasLabel, r.hasHidden]),
        presenceFromJson: fromJson.records.map(r => [r.hasBonus, r.hasLabel, r.hasHidden]),
        bonusFromBinary: fromBinary.records.map(r => r.bonus),
        bonusFromJson: fromJson.records.map(r => r.bonus),
    }))

    console.log(JSON.stringify({ mismatches }))

    return mismatches.length === 0 ? 0 : 1
}

process.exit(main())
