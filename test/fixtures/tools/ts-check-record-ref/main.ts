// Round-trip check for a reference that is a member of a record group.
//
// Read through the accessor rather than a table at a time, because resolution is the
// accessor's: a table read on its own has the keys and nothing to look them up in. That is
// also what makes this worth running - the linking pass is generated per shape, and one
// written around the wrong element index compiles and resolves the wrong row.
//
// All three record shapes, because each puts the element number somewhere else: an array of
// records indexes the group, a record of one indexes nothing, and a record of arrays indexes
// the member.
//
// Three routes over the same cells. The named JSON carries the key under the member's own
// name; the binary carries one column per member; the compact JSON is positional over the
// wire columns, which is the route most likely to be wrong.
//
// spec/references/references-in-records.md.
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
 * The target's name rather than its key, so a reference that resolved to the wrong row shows
 * as a different word - comparing the key back would pass whatever the linking pass did.
 */
function resolved(row: { name: string } | undefined): string {
    return row ? row.name : '<unresolved>'
}

function main(): number {
    const jsonDir = process.argv[2]
    const binaryDir = process.argv[3]

    if (!jsonDir || !binaryDir) {
        console.error('usage: ts-check-record-ref <json-dir> <binary-dir>')
        return 2
    }

    const fromJson = new Tables()
    fromJson.readAllSync(jsonDir, '.json')

    const fromBinary = new Tables()
    fromBinary.readAllBinarySync(binaryDir, '.tcb')

    const fromCompact = new Tables()
    fromCompact.readAllSync(`${jsonDir}/../json-compact`, '.json')

    // An array of records: the element number is on the group.
    for (let i = 0; i < fromJson.loadout.records.length; i++) {
        const j = fromJson.loadout.records[i]
        const b = fromBinary.loadout.records[i]
        const c = fromCompact.loadout.records[i]

        compare('Loadout', i, 'index', j.index, b.index)
        compare('Loadout', i, 'slot.length', j.slot.length, b.slot.length)

        for (let k = 0; k < j.slot.length; k++) {
            compare('Loadout', i, `slot[${k}].itemId`, j.slot[k].itemId, b.slot[k].itemId)
            compare('Loadout', i, `slot[${k}].itemByItemId`,
                    resolved(j.slot[k].itemByItemId),
                    resolved(b.slot[k].itemByItemId))
            compare('Loadout', i, `slot[${k}].count`, j.slot[k].count, b.slot[k].count)

            // The second reference of the same element, at the same table. A key named after
            // the group and the target would be one name for both.
            compare('Loadout', i, `slot[${k}].itemBySwapId`,
                    resolved(j.slot[k].itemBySwapId),
                    resolved(b.slot[k].itemBySwapId))

            compare('Loadout', i, `compact slot[${k}].itemByItemId`,
                    resolved(c.slot[k].itemByItemId),
                    resolved(b.slot[k].itemByItemId))
            compare('Loadout', i, `compact slot[${k}].itemBySwapId`,
                    resolved(c.slot[k].itemBySwapId),
                    resolved(b.slot[k].itemBySwapId))
            compare('Loadout', i, `compact slot[${k}].count`, c.slot[k].count, b.slot[k].count)
        }
    }

    // A reference two levels in: the member is named by its whole path.
    for (let i = 0; i < fromJson.mount.records.length; i++) {
        const j = fromJson.mount.records[i]
        const b = fromBinary.mount.records[i]
        const c = fromCompact.mount.records[i]

        for (let k = 0; k < j.rig.length; k++) {
            compare('Mount', i, `rig[${k}].core.itemByItemId`,
                    resolved(j.rig[k].core.itemByItemId),
                    resolved(b.rig[k].core.itemByItemId))
            compare('Mount', i, `compact rig[${k}].core.itemByItemId`,
                    resolved(c.rig[k].core.itemByItemId),
                    resolved(b.rig[k].core.itemByItemId))
            compare('Mount', i, `rig[${k}].core.count`, j.rig[k].core.count, b.rig[k].core.count)
        }
    }

    // A key that is not a number, and the empty one that points at nothing.
    for (let i = 0; i < fromJson.pose.records.length; i++) {
        const j = fromJson.pose.records[i]
        const b = fromBinary.pose.records[i]
        const c = fromCompact.pose.records[i]

        for (let k = 0; k < j.step.length; k++) {
            compare('Pose', i, `step[${k}].clipId`, j.step[k].clipId, b.step[k].clipId)
            compare('Pose', i, `step[${k}].clipByClipId`,
                    j.step[k].clipByClipId ? j.step[k].clipByClipId!.index : '<unresolved>',
                    b.step[k].clipByClipId ? b.step[k].clipByClipId!.index : '<unresolved>')
            compare('Pose', i, `compact step[${k}].clipId`,
                    c.step[k].clipId, b.step[k].clipId)
        }
    }

    // A trimmed group: the length is this row's rather than the sheet's.
    for (let i = 0; i < fromJson.kit.records.length; i++) {
        const j = fromJson.kit.records[i]
        const b = fromBinary.kit.records[i]
        const c = fromCompact.kit.records[i]

        compare('Kit', i, 'part.length', j.part.length, b.part.length)
        compare('Kit', i, 'compact part.length', c.part.length, b.part.length)

        for (let k = 0; k < j.part.length; k++) {
            compare('Kit', i, `part[${k}].itemByItemId`,
                    resolved(j.part[k].itemByItemId),
                    resolved(b.part[k].itemByItemId))
            compare('Kit', i, `compact part[${k}].itemByItemId`,
                    resolved(c.part[k].itemByItemId),
                    resolved(b.part[k].itemByItemId))
            compare('Kit', i, `part[${k}].count`, j.part[k].count, b.part[k].count)
        }
    }

    // A record of one: no element number anywhere.
    for (let i = 0; i < fromJson.holder.records.length; i++) {
        const j = fromJson.holder.records[i]
        const b = fromBinary.holder.records[i]
        const c = fromCompact.holder.records[i]

        compare('Holder', i, 'main.itemId', j.main.itemId, b.main.itemId)
        compare('Holder', i, 'main.itemByItemId',
                resolved(j.main.itemByItemId),
                resolved(b.main.itemByItemId))
        compare('Holder', i, 'main.count', j.main.count, b.main.count)

        compare('Holder', i, 'compact main.itemByItemId',
                resolved(c.main.itemByItemId),
                resolved(b.main.itemByItemId))
    }

    // A record of arrays: the element number is on the member.
    for (let i = 0; i < fromJson.bag.records.length; i++) {
        const j = fromJson.bag.records[i]
        const b = fromBinary.bag.records[i]
        const c = fromCompact.bag.records[i]

        compare('Bag', i, 'slots.itemByItemId.length', j.slots.itemByItemId.length, b.slots.itemByItemId.length)

        for (let k = 0; k < j.slots.itemByItemId.length; k++) {
            compare('Bag', i, `slots.itemId[${k}]`, j.slots.itemId[k], b.slots.itemId[k])
            compare('Bag', i, `slots.itemByItemId[${k}]`,
                    resolved(j.slots.itemByItemId[k]),
                    resolved(b.slots.itemByItemId[k]))
            compare('Bag', i, `slots.count[${k}]`, j.slots.count[k], b.slots.count[k])

            compare('Bag', i, `compact slots.itemByItemId[${k}]`,
                    resolved(c.slots.itemByItemId[k]),
                    resolved(b.slots.itemByItemId[k]))
        }
    }

    // What the records actually hold, so the harness can assert on values rather than only on
    // the routes agreeing - they could agree and all be wrong. Element 0 and element 1 point
    // at different rows, so an element index that is off shows here.
    console.log(JSON.stringify({
        loadout: fromBinary.loadout.records.map(
            r => r.slot.map(s => `${resolved(s.itemByItemId)}+${resolved(s.itemBySwapId)}`).join('/')),
        holder: fromBinary.holder.records.map(
            r => resolved(r.main.itemByItemId)),
        bag: fromBinary.bag.records.map(
            r => r.slots.itemByItemId.map((_, k) => resolved(r.slots.itemByItemId[k])).join('/')),
        mount: fromBinary.mount.records.map(
            r => r.rig.map(g => resolved(g.core.itemByItemId)).join('/')),
        pose: fromBinary.pose.records.map(
            r => r.step.map(s => s.clipByClipId ? s.clipByClipId!.index : '<unresolved>').join('/')),
        kit: fromBinary.kit.records.map(
            r => r.part.map(p => resolved(p.itemByItemId)).join('/')),
    }))

    console.log(JSON.stringify({ mismatches }))

    return mismatches.length === 0 ? 0 : 1
}

process.exit(main())
