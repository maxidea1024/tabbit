// Drives one update and prints what it did, for the C# test to assert against.
//
// The updater under test is the shipped one - lib/dart/tabbit/updater.dart - copied in
// beside this file and imported exactly as a consumer would import it.

import 'dart:convert';
import 'dart:io';

import 'tabbit/updater.dart' as tabbit;

Future<void> main(List<String> args) async {
  if (args.length < 2) {
    stderr.writeln('usage: main.dart <base-url> <cache-directory>');
    exit(2);
  }

  final result = await tabbit.update(
    args[0],
    args[1],
    tabbit.UpdateOptions(
      // Short, because the retry test would otherwise spend its time asleep.
      retryDelay: const Duration(milliseconds: 50),
      log: stderr.writeln,
    ),
  );

  print(json.encode(<String, Object?>{
    'succeeded': result.succeeded,
    'error': result.error,
    'upToDate': result.upToDate,
    'downloadedCount': result.downloadedCount,
    'downloadedBytes': result.downloadedBytes,
    'deletedCount': result.deletedCount,
    'localPath': result.localPath,
  }));
}
