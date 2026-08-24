// Round-trip check for the TypeScript binary reader.
//
// Loads the same tables twice - once from the JSON export and once from the binary
// export - and reports any field the two disagree on. That is the property that
// matters: a generated table exposes one API, so both read paths have to produce
// the same values, and only running them side by side can show that they do.
//
// Prints JSON on stdout for the C# harness to assert against.

import { ArrayTypesTable } from './generated/tables/array-types'
import { ItemTable } from './generated/tables/item'
import { LocalizationTable } from './generated/tables/localization'
import { TestFieldTypesTable } from './generated/tables/test-field-types'
import { Tables } from './generated/tables'

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
 * Everything goes to text: a BigInt and a number that happen to hold the same
 * value should compare equal, and an array should compare by its elements.
 */
function render(value: unknown): string {
    if (Array.isArray(value)) return '[' + value.map(render).join(',') + ']'
    if (typeof value === 'bigint') return value.toString()
    if (value === null || value === undefined) return String(value)
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
        console.error('usage: ts-check <json-dir> <binary-dir>')
        return 2
    }

    // --- every primitive type -------------------------------------------------
    {
        const fromJson = new TestFieldTypesTable()
        fromJson.readSync(`${jsonDir}/TestFieldTypes.json`)

        const fromBinary = new TestFieldTypesTable()
        fromBinary.readBinarySync(`${binaryDir}/TestFieldTypes.tcb`)

        for (let i = 0; i < fromJson.records.length; i++) {
            const j = fromJson.records[i]
            const b = fromBinary.records[i]

            compare('TestFieldTypes', i, 'index', j.index, b.index)
            compare('TestFieldTypes', i, 'stringField', j.stringField, b.stringField)
            compare('TestFieldTypes', i, 'boolField', j.boolField, b.boolField)
            compare('TestFieldTypes', i, 'intField', j.intField, b.intField)
            compare('TestFieldTypes', i, 'bigIntField', j.bigIntField, b.bigIntField)
            compare('TestFieldTypes', i, 'floatField', j.floatField, b.floatField)
            compare('TestFieldTypes', i, 'doubleField', j.doubleField, b.doubleField)
            compare('TestFieldTypes', i, 'datetimeField', j.datetimeField, b.datetimeField)
            compare('TestFieldTypes', i, 'timespanField', j.timespanField, b.timespanField)
            compare('TestFieldTypes', i, 'uuidField', j.uuidField, b.uuidField)
            compare('TestFieldTypes', i, 'valueTypeField', j.valueTypeField, b.valueTypeField)
        }

        // Reported separately, because a 64-bit value is the one case where the two
        // sources genuinely cannot agree unless both go through BigInt.
        console.log(JSON.stringify({
            bigIntFromBinary: fromBinary.records.map(r => r.bigIntField.toString()),
            bigIntFromJson: fromJson.records.map(r => r.bigIntField.toString()),
        }))
    }

    // --- both array kinds -----------------------------------------------------
    {
        const fromJson = new ArrayTypesTable()
        fromJson.readSync(`${jsonDir}/ArrayTypes.json`)

        const fromBinary = new ArrayTypesTable()
        fromBinary.readBinarySync(`${binaryDir}/ArrayTypes.tcb`)

        for (let i = 0; i < fromJson.records.length; i++) {
            const j = fromJson.records[i]
            const b = fromBinary.records[i]

            compare('ArrayTypes', i, 'tags', j.tags, b.tags)
            compare('ArrayTypes', i, 'costs', j.costs, b.costs)
            compare('ArrayTypes', i, 'weights', j.weights, b.weights)
            compare('ArrayTypes', i, 'grades', j.grades, b.grades)
            compare('ArrayTypes', i, 'slot', j.slot, b.slot)
        }
    }

    // --- serial fields --------------------------------------------------------
    {
        const fromJson = new LocalizationTable()
        fromJson.readSync(`${jsonDir}/Localization.json`)

        const fromBinary = new LocalizationTable()
        fromBinary.readBinarySync(`${binaryDir}/Localization.tcb`)

        for (let i = 0; i < fromJson.records.length; i++) {
            compare('Localization', i, 'textEn',
                fromJson.records[i].textEn, fromBinary.records[i].textEn)
            compare('Localization', i, 'textKo',
                fromJson.records[i].textKo, fromBinary.records[i].textKo)
        }
    }

    // --- references, read one table at a time ---------------------------------
    //
    // Unlinked on purpose: a table read on its own has the key and not the row, which is
    // what the accessor below is for.
    {
        const fromBinary = new ItemTable()
        fromBinary.readBinarySync(`${binaryDir}/Item.tcb`)

        console.log(JSON.stringify({
            itemNames: fromBinary.records.map(r => r.name),
            categoryIndices: fromBinary.records.map(r => r._categoryId_ItemCategory_index),
        }))
    }

    // --- references, linked by the accessor -----------------------------------
    //
    // Through `Tables` rather than one table at a time, because linking needs every table
    // and only the accessor has them. Both formats go through it, so this asks the same
    // question of references that the loops above ask of values: do the two paths agree.
    //
    // They did not. `solveCrossReferences` was generated empty, so nothing ever called the
    // `setReference_*_INTERNAL` methods: the binary path left `categoryId` undefined and
    // the JSON path assigned the raw key into it - a number in a member typed as a row.
    {
        const fromJson = new Tables()
        fromJson.readAllSync(jsonDir)

        const fromBinary = new Tables()
        fromBinary.readAllBinarySync(binaryDir)

        for (let i = 0; i < fromJson.item.records.length; i++) {
            const j = fromJson.item.records[i]
            const b = fromBinary.item.records[i]

            // The row a reference resolves to, named by a field only the row has.
            compare('Item', i, 'categoryId.name', j.categoryId?.name, b.categoryId?.name)
            compare('Item', i, 'categoryId(key)',
                j._categoryId_ItemCategory_index, b._categoryId_ItemCategory_index)
            compare('Item', i, 'categoryId(linked)', j._categoryId_F, b._categoryId_F)
        }

        console.log(JSON.stringify({
            linkedCategoryNames: fromBinary.item.records.map(r => r.categoryId?.name ?? null),
        }))
    }

    console.log(JSON.stringify({ mismatches }))

    return mismatches.length === 0 ? 0 : 1
}

process.exit(main())
