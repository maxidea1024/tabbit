<?php

/**
 * Drives one update and prints what it did, for the C# test to assert against.
 *
 * The updater under test is the shipped one - lib/php/tabbit/TabbitUpdater.php -
 * copied in beside this file and required exactly as a consumer would require it.
 */

declare(strict_types=1);

require_once __DIR__ . '/tabbit/TabbitUpdater.php';

use Tabbit\TabbitUpdater;
use Tabbit\UpdateOptions;

if ($argc < 3) {
    \fwrite(\STDERR, "usage: main.php <base-url> <cache-directory>\n");
    exit(2);
}

$options = new UpdateOptions();

// Short, because the retry test would otherwise spend its time asleep.
$options->retryDelay = 0.05;
$options->log = static function (string $message): void {
    \fwrite(\STDERR, $message . "\n");
};

$result = TabbitUpdater::update($argv[1], $argv[2], $options);

echo \json_encode([
    'succeeded' => $result->succeeded,
    'error' => $result->error,
    'upToDate' => $result->upToDate,
    'downloadedCount' => $result->downloadedCount,
    'downloadedBytes' => $result->downloadedBytes,
    'deletedCount' => $result->deletedCount,
    'localPath' => $result->localPath,
], \JSON_UNESCAPED_SLASHES), "\n";
