<?php

/*
 * Read-back check for a sealed binary export, in PHP.
 *
 * The counterpart of tools/cs-check-encrypted: the same fixture, the same sealed files, a
 * reader written independently of the C# one. What it demonstrates is the whole of what a
 * consuming PHP project has to do about encryption - set the accessor's `$encryptionKey`
 * once, before the first read, and call the same `readAll` an unencrypted project calls.
 *
 * PHP is worth checking separately because its ChaCha20 comes from ext-openssl rather than
 * from the language, and an envelope opener written against a function the runtime does not
 * have looks exactly like one that works until a sealed file reaches it.
 *
 * The keys arrive as arguments rather than being written here, which is also how they are
 * meant to reach a real client - from wherever that project keeps secrets, at start-up.
 * Without them the load is expected to fail, and this prints why, so the test can assert
 * that the reader names the cause itself.
 *
 * Two keys, because the two layers are independent: the first seals the file and the second
 * says it is the file that was exported.
 *
 * spec/tcb-v104-composed-encodings.md section 4 · spec/tcb-mac-and-signature.md.
 */

declare(strict_types=1);

require_once __DIR__ . '/EncryptedData.php';

use Encrypted\EncryptedData;

if ($argc < 2) {
    \fwrite(\STDERR, "usage: harness.php <binary-table-directory> [hex-key] [hex-mac-key]\n");
    exit(2);
}

if ($argc > 2 && $argv[2] !== '') {
    // Raw bytes, not hex: the accessor takes the thirty-two bytes the converter was given.
    $key = \hex2bin($argv[2]);

    if ($key === false) {
        \fwrite(\STDERR, "The key argument is not hex.\n");
        exit(2);
    }

    EncryptedData::$encryptionKey = $key;
}

if ($argc > 3 && $argv[3] !== '') {
    $macKey = \hex2bin($argv[3]);

    if ($macKey === false) {
        \fwrite(\STDERR, "The MAC key argument is not hex.\n");
        exit(2);
    }

    EncryptedData::$macKey = $macKey;
}

$data = new EncryptedData();

try {
    $data->readAll($argv[1]);
} catch (\Throwable $error) {
    // The message alone, not the trace: what the test is asking is whether the reader said
    // why, and a trace would let any failure satisfy that.
    \fwrite(\STDERR, $error->getMessage() . "\n");
    exit(1);
}

$rows = [];

foreach ($data->animation->records as $record) {
    $rows[] = [
        'index' => $record->index,

        // Printed round-trippably and compared as a float on the other side, so the
        // assertion is about the value rather than about how JSON renders one.
        'blend' => \var_export($record->blend, true),

        'slot' => $record->slot,
    ];
}

echo \json_encode($rows);
