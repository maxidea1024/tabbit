<?php

/*
 * Conformance harness for the generated PHP reader.
 *
 * Reads Vectors.tcb through the generated accessor and prints each row in the canonical
 * form described in ../README.md. No parsing here: the generated reader does that.
 */

declare(strict_types=1);

require_once __DIR__ . '/ConformanceData.php';

use Conformance\ConformanceData;

/*
 * A JSON string, escaped by hand.
 *
 * Not json_encode: it escapes non-ASCII to \uXXXX by default and the corpus holds
 * characters outside the basic plane, so the comparison would be against PHP's idea of an
 * escape rather than against the bytes the exporter wrote.
 */
function quoted(string $value): string
{
    $out = '"';

    $length = \strlen($value);

    for ($i = 0; $i < $length; $i++) {
        $c = $value[$i];
        $code = \ord($c);

        if ($c === '"') {
            $out .= '\\"';
        } elseif ($c === '\\') {
            $out .= '\\\\';
        } elseif ($c === "\n") {
            $out .= '\\n';
        } elseif ($c === "\r") {
            $out .= '\\r';
        } elseif ($c === "\t") {
            $out .= '\\t';
        } elseif ($code < 0x20) {
            $out .= \sprintf('\\u%04x', $code);
        } else {
            // UTF-8 bytes straight through, which is what the exporter wrote.
            $out .= $c;
        }
    }

    return $out . '"';
}

/*
 * A double with enough digits to survive the round trip.
 *
 * PHP's default precision rounds to 14 significant digits, which loses the corpus's
 * float32 boundary values. serialize_precision = -1 means "as many as it takes and no
 * more", which is what var_export and json_encode use.
 */
function number(float $value): string
{
    return \var_export($value, true);
}

if ($argc < 2) {
    \fwrite(\STDERR, "usage: harness.php <binary-directory>\n");
    exit(1);
}

// The corpus is signed, so the key goes in before the first read - which is the whole of
// what a consuming project does about the MAC. Without it the files would still load, and
// nothing here would notice: the check is the reader's, and it needs the key to run.
$macKey = \getenv('TABBIT_TEST_TCB_MAC_KEY');

if ($macKey !== false && $macKey !== '') {
    ConformanceData::$macKey = \hex2bin($macKey);
}

$data = new ConformanceData();
$data->readAll($argv[1]);

$parts = [];

foreach ($data->vectors->records as $r) {
    $row = '{';
    $row .= '"index":' . $r->index . ',';
    $row .= '"intVal":' . $r->intVal . ',';

    // A string, because JSON's single numeric type would round anything past 2^53.
    $row .= '"bigVal":"' . $r->bigVal . '",';

    $row .= '"floatVal":' . number($r->floatVal) . ',';
    $row .= '"doubleVal":' . number($r->doubleVal) . ',';
    $row .= '"text":' . quoted($r->text) . ',';
    $row .= '"flag":' . ($r->flag ? 'true' : 'false') . ',';

    // Ticks, which is what the generated fields hold.
    $row .= '"when":"' . $r->when . '",';
    $row .= '"span":"' . $r->span . '",';

    $row .= '"uid":"' . (string)$r->uid . '",';
    $row .= '"label":' . $r->label->value . ',';

    $row .= '"ints":[' . \implode(',', \array_map('strval', $r->ints)) . '],';
    $row .= '"strs":[' . \implode(',', \array_map('quoted', $r->strs)) . ']';
    // The two array forms whose element read is not the scalar one in a loop.
    $row .= ',"labels":[' . \implode(',', \array_map(static fn ($v) => $v->value, $r->labels)) . ']';
    $row .= ',"uids":[' . \implode(',', \array_map(static fn ($v) => quoted((string)$v), $r->uids)) . ']';
    // The reference indices, which is what the exporter writes for a foreign field.
    $row .= ',"owner":' . $r->owner;
    $row .= ',"tier":' . $r->tierIndex;
    // And one reference per element, printed as the stored index each came in as.
    $row .= ',"owners":[' . \implode(',', \array_map('strval', $r->owners)) . ']';
    // The three the v104 encodings win on.
    $row .= ',"count":' . number($r->count);
    $row .= ',"route":' . quoted($r->route);
    $row .= ',"zone":' . quoted($r->zone);
    $row .= '}';

    $parts[] = $row;
}

echo '[' . \implode(',', $parts) . ']';
