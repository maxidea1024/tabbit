// Conformance harness for the generated TypeScript reader.
//
// Reads Vectors.tcb through the generated table class and prints each row in the
// canonical form described in ../README.md. No parsing here: the generated reader does
// that.

declare function require(moduleName: string): any
declare const process: any

const path = require('path')

import { VectorsTable } from './tables/vectors'
import { Tables } from './tables'

const binaryDir: string = process.argv[2]
if (!binaryDir) {
    process.stderr.write('usage: main.ts <binary-directory>\n')
    process.exit(1)
}

// The corpus is signed, so the key goes in before the first read - which is the whole of
// what a consuming project does about the MAC. Without it the files would still load, and
// nothing here would notice: the check is the reader's, and it needs the key to run.
const macKeyText: string = process.env.TABBIT_TEST_TCB_MAC_KEY

if (macKeyText) {
    const key = new Uint8Array(macKeyText.length / 2)

    for (let at = 0; at < key.length; ++at) {
        key[at] = parseInt(macKeyText.substr(at * 2, 2), 16)
    }

    Tables.macKey = key
}

const table = new VectorsTable()
table.readBinarySync(path.join(binaryDir, 'Vectors.tcb'))

// Ticks rather than the reader's formatted strings: the contract asks for the exact
// value, and a tick count has no formatting to disagree about.
const TICKS_PER_SECOND = 10000000n
const EPOCH_TICKS = 621355968000000000n

function dateTimeTicks(text: string): string {
    // The reader hands back the same string the JSON export writes, so this is the one
    // place the harness has to undo a formatting step.
    const millis = BigInt(Date.parse(text + 'Z'))
    const fraction = text.includes('.') ? text.split('.')[1].padEnd(7, '0').slice(0, 7) : '0'
    return (EPOCH_TICKS + millis / 1000n * TICKS_PER_SECOND + BigInt(fraction)).toString()
}

function timeSpanTicks(text: string): string {
    const negative = text.startsWith('-')
    const body = negative ? text.slice(1) : text

    const [dayPart, timePart] = body.includes('.') && body.indexOf('.') < body.indexOf(':')
        ? [body.split('.')[0], body.slice(body.indexOf('.') + 1)]
        : ['0', body]

    const [h, m, rest] = timePart.split(':')
    const [s, frac] = rest.includes('.') ? rest.split('.') : [rest, '0']

    const total =
        BigInt(dayPart) * 864000000000n +
        BigInt(h) * 36000000000n +
        BigInt(m) * 600000000n +
        BigInt(s) * TICKS_PER_SECOND +
        BigInt(frac.padEnd(7, '0').slice(0, 7))

    return (negative ? -total : total).toString()
}

const rows = table.records.map(r => ({
    index: r.index,
    intVal: r.intVal,
    bigVal: r.bigVal.toString(),
    floatVal: r.floatVal,
    doubleVal: r.doubleVal,
    text: r.text,
    flag: r.flag,
    when: dateTimeTicks(r.when),
    span: timeSpanTicks(r.span),
    uid: r.uid.toLowerCase(),
    label: r.label as unknown as number,
    ints: r.ints,
    strs: r.strs,

    // The two array forms whose element read is not the scalar one in a loop.
    labels: r.labels as unknown as number[],
    uids: r.uids.map((value) => value.toLowerCase()),

    // The reference indices, which is what the exporter writes for a foreign field.
    owner: r._owner_Owners_index,
    tier: r._tier_Owners_index,

    // And one reference per element, printed as the stored index each came in as.
    owners: r._owners_Owners_index,

    // The three the v104 encodings win on.
    count: r.count,
    route: r.route,
    zone: r.zone,
}))

process.stdout.write(JSON.stringify(rows))
