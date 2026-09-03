// Round-trip check for a polymorphic record group.
//
// **The type check is most of this.** A discriminated union only means something if narrowing
// on `kind` actually reaches each variant's own members, and that is a claim about generated
// code which the compiler settles - a generator emitting the union flat would produce
// something this file cannot type-check against.
//
// What the read adds: that the discriminator picked the right variant per row, and that a
// member belonging to another variant is not on the narrowed type at all. The second is the
// one the union notation makes easy to get wrong, because every row has blank cells that are
// not its own.
//
// spec/types/polymorphism.md sections 5.2 and 7.
//
// Prints JSON on stdout for the C# harness to assert against.

import * as fs from 'fs'

import { Tables } from './generated/tables'
import { Effect } from './generated/structs/effect'

/** What one effect carries beyond the base field, per variant. */
function own(effect: Effect): string {
    // Narrowing on `kind`. Each branch reaches members the others do not have, so a union
    // that did not narrow - or a variant missing a member - fails to compile here.
    switch (effect.kind) {
        case 'DamageEffect':
            return `damage=${effect.damage},pierces=${effect.pierces}`
        case 'HealEffect':
            return `amount=${effect.amount}`
        case 'NoEffect':
            return 'none'
    }
}

function main(): void {
    // argv[2] is the JSON directory and argv[3] the binary one, which is the order every
    // driver here is handed. Only the binary is read: what this checks is the narrowing, and
    // the two routes agreeing is `PolymorphicRecordTests`' own JSON assertions.
    const binaryDir = process.argv[3]

    const tables = new Tables()
    tables.readAllBinarySync(name => new Uint8Array(fs.readFileSync(`${binaryDir}/${name}`)), '.tcb')

    const rows = tables.skill.records.map(row => ({
        index: row.index,
        name: row.name,

        // The variant, named by the union's own discriminant rather than by the number.
        kind: row.effect.kind,

        // The abstract type's own field, read through the union - which is the whole point of
        // it being one column.
        chance: row.effect.chance,

        own: own(row.effect),
    }))

    // And the array of them, where each element is its own shape. The getter hands back a
    // list of the union, so `own` - which narrows - takes each element unchanged.
    // spec/types/polymorphism.md section 5.3.
    const combos = tables.combo.records.map(row => ({
        name: row.name,
        kinds: row.effects.map(effect => effect.kind).join(','),
        own: row.effects.map(own).join(','),
    }))

    console.log(JSON.stringify({ Skill: rows, Combo: combos }))
}

try {
    main()
} catch (error) {
    console.error(error)
    process.exit(1)
}
