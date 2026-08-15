// Tabbit's data updater.
//
// Brings a local copy of the exported data up to date with a copy served over HTTP - a
// CDN, a bucket, a patch server - so a running program can take new data without being
// redeployed. Emitted beside the reader and reads nothing but the manifest, so it knows
// nothing about the schema and never has to change when one does.
//
// The manifest is what the exporter already writes next to the data: one entry per file
// with its size and MD5. Comparing it with the local copy is the whole of the diff, so a
// run downloads what changed and nothing else.
//
// Three properties, because a patcher that fails badly is worse than one that does not
// exist:
//
//   Nothing is replaced until everything has arrived and been checked. Files land in a
//   staging directory first and the local manifest is written last, so an update killed
//   halfway leaves the previous data readable and the next run redoes the difference.
//
//   Every file is checked against the hash the manifest gives for it, so a truncated
//   transfer that a proxy reported as success does not reach the cache.
//
//   A transient failure is retried with a doubling backoff, and a permanent one is not.
//
// Reading is somebody else's job. This produces a directory, and the generated tables
// read it.
//
// The SDK and nothing else - which is why the MD5 below is written out rather than taken
// from package:crypto. The Dart SDK has no MD5 and the generated output promises to need
// no packages, so the algorithm is here: sixty lines of something fixed, against a
// dependency in every consumer's pubspec. A wrong one could not hide either - every
// download would fail its hash check on the first run.

import 'dart:convert';
import 'dart:io';
import 'dart:math' as math;
import 'dart:typed_data';

/// What an update is allowed to do. Every value has a working default.
class UpdateOptions {
  const UpdateOptions({
    this.manifestFileName = 'manifest-binary.json',
    this.maxAttempts = 3,
    this.retryDelay = const Duration(milliseconds: 500),
    this.requestTimeout = const Duration(seconds: 30),
    this.verifyHash = true,
    this.log,
  });

  /// The binary exporter writes manifest-binary.json; the JSON exporter writes
  /// manifest-json.json.
  final String manifestFileName;

  /// The first attempt is included, so three is two retries.
  final int maxAttempts;

  /// Waited before the second attempt. Doubled for each attempt after it.
  final Duration retryDelay;

  final Duration requestTimeout;
  final bool verifyHash;

  /// Called with one line of progress, when given.
  final void Function(String message)? log;
}

/// What an update did.
class UpdateResult {
  UpdateResult(this.localPath);

  bool succeeded = false;
  String? error;
  bool upToDate = false;
  int downloadedCount = 0;
  int downloadedBytes = 0;
  int deletedCount = 0;

  /// The directory holding the data. Hand it to the generated tables' readAll. Set even on
  /// failure, because the previous data is still there and still readable - which is the
  /// point of failing the way this does.
  final String localPath;
}

/// One file of the manifest, and the hash to check it by.
class ManifestEntry {
  const ManifestEntry(this.name, this.size, this.hash);

  final String name;
  final int size;
  final String hash;
}

/// A failure the same request might survive a moment later.
class TransientError implements Exception {
  TransientError(this.message);

  final String message;

  @override
  String toString() => message;
}

/// Reads the entries out of a manifest's JSON.
List<ManifestEntry> parseManifest(String text) {
  final manifest = json.decode(text);

  if (manifest is! Map || manifest['Items'] is! List) {
    throw const FormatException('the manifest has no Items array');
  }

  final entries = <ManifestEntry>[];

  for (final item in manifest['Items'] as List) {
    if (item is! Map) continue;

    final name = item['Name'];
    if (name is! String || name.isEmpty) continue;

    entries.add(ManifestEntry(
        name, (item['Size'] as num?)?.toInt() ?? 0, (item['Hash'] as String?) ?? ''));
  }

  return entries;
}

/// Brings [cacheDirectory] up to date with the data served under [baseUrl].
///
/// Does not throw. Everything that can go wrong here - the network, the disk, a file that
/// arrived corrupt - is a condition the caller has to handle rather than a defect, and a
/// patcher that throws into a program's startup is one that gets wrapped in a bare catch
/// that swallows the reason.
Future<UpdateResult> update(String baseUrl, String cacheDirectory,
    [UpdateOptions options = const UpdateOptions()]) async {
  final result = UpdateResult(cacheDirectory);
  void log(String message) => options.log?.call(message);

  final client = HttpClient()..connectionTimeout = options.requestTimeout;

  try {
    final manifestBytes =
        await _download(client, _joinUrl(baseUrl, options.manifestFileName), options, log);
    final manifestText = utf8.decode(manifestBytes);

    final remote = parseManifest(manifestText);
    final local = _readLocalManifest(
        File('$cacheDirectory${Platform.pathSeparator}${options.manifestFileName}'));

    final byName = <String, ManifestEntry>{for (final entry in local) entry.name: entry};

    final wanted = <ManifestEntry>[];

    for (final entry in remote) {
      final previous = byName[entry.name];

      // The file's presence is checked as well as the manifest's word for it: a cache
      // somebody cleaned out by hand would otherwise never be refilled.
      final current = previous != null &&
          previous.hash == entry.hash &&
          File(_localPath(cacheDirectory, entry.name)).existsSync();

      if (!current) wanted.add(entry);
    }

    final served = {for (final entry in remote) entry.name};
    final gone = [for (final entry in local) if (!served.contains(entry.name)) entry.name];

    if (wanted.isEmpty && gone.isEmpty) {
      log('tabbit: already up to date.');

      result.succeeded = true;
      result.upToDate = true;
      return result;
    }

    log('tabbit: ${wanted.length} file(s) to fetch, ${gone.length} to remove.');

    // Everything lands here first. Nothing the caller can read is touched until the last
    // file has arrived and been checked.
    final staging = Directory(_localPath(cacheDirectory, '.staging'));

    Directory(cacheDirectory).createSync(recursive: true);
    if (staging.existsSync()) staging.deleteSync(recursive: true);
    staging.createSync(recursive: true);

    for (final entry in wanted) {
      final data = await _download(client, _joinUrl(baseUrl, entry.name), options, log);

      if (options.verifyHash && entry.hash.isNotEmpty) {
        final actual = md5Hex(data);

        if (actual.toLowerCase() != entry.hash.toLowerCase()) {
          throw StateError("'${entry.name}' arrived with hash $actual, and the manifest "
              'says ${entry.hash}. Nothing was replaced.');
        }
      }

      final staged = File(_localPath(staging.path, entry.name));

      staged.parent.createSync(recursive: true);
      staged.writeAsBytesSync(data);

      result.downloadedBytes += data.length;
    }

    // From here on the update is applied. Nothing below reaches the network.
    for (final name in gone) {
      final target = File(_localPath(cacheDirectory, name));

      if (target.existsSync()) target.deleteSync();
      result.deletedCount += 1;
    }

    for (final entry in wanted) {
      final target = File(_localPath(cacheDirectory, entry.name));

      target.parent.createSync(recursive: true);
      if (target.existsSync()) target.deleteSync();

      File(_localPath(staging.path, entry.name)).renameSync(target.path);
      result.downloadedCount += 1;
    }

    // Last, and that ordering is the recovery story: a run killed before this point leaves
    // a manifest describing the data that is still on disk, so the next run fetches the
    // same files again rather than believing it has them.
    File(_localPath(cacheDirectory, options.manifestFileName))
        .writeAsStringSync(manifestText, encoding: utf8);

    if (staging.existsSync()) staging.deleteSync(recursive: true);

    log('tabbit: updated. ${result.downloadedCount} fetched, '
        '${result.deletedCount} removed.');

    result.succeeded = true;
    return result;
  } catch (error) {
    // The previous data is untouched, so the caller can carry on with it.
    result.error = error is TransientError ? error.message : error.toString();

    log('tabbit: update failed: ${result.error}');
    return result;
  } finally {
    client.close(force: true);
  }
}

/// Fetches one URL, retrying what is worth retrying.
Future<Uint8List> _download(HttpClient client, String url, UpdateOptions options,
    void Function(String) log) async {
  var delay = options.retryDelay;
  final attempts = options.maxAttempts < 1 ? 1 : options.maxAttempts;

  for (var attempt = 1;; attempt++) {
    try {
      return await _fetch(client, url, options);
    } on TransientError catch (error) {
      if (attempt >= attempts) rethrow;

      log('tabbit: ${error.message} Retrying in '
          '${(delay.inMilliseconds / 1000).toStringAsFixed(1)}s ($attempt of $attempts).');

      await Future<void>.delayed(delay);

      // Doubling rather than a fixed wait: a server refusing because it is overloaded is
      // not helped by every client coming back at the same interval.
      delay *= 2;
    }
  }
}

Future<Uint8List> _fetch(HttpClient client, String url, UpdateOptions options) async {
  try {
    final request = await client.getUrl(Uri.parse(url)).timeout(options.requestTimeout);
    final response = await request.close().timeout(options.requestTimeout);

    final builder = BytesBuilder(copy: false);
    await for (final chunk in response) {
      builder.add(chunk);
    }

    if (response.statusCode >= 200 && response.statusCode < 300) {
      return builder.takeBytes();
    }

    final message = "'$url' answered ${response.statusCode} ${response.reasonPhrase}.";

    // 408 and 429 are the server asking for another attempt, and 5xx is it failing on its
    // own account. A 404 is an answer: retrying it costs three round trips to hear the
    // same thing.
    if (response.statusCode == 408 ||
        response.statusCode == 429 ||
        (response.statusCode >= 500 && response.statusCode <= 599)) {
      throw TransientError(message);
    }

    throw StateError(message);
  } on SocketException catch (error) {
    // The request never got an answer - DNS, a refused connection.
    throw TransientError("'$url' could not be reached: ${error.message}.");
  } on HttpException catch (error) {
    throw TransientError("'$url' could not be reached: ${error.message}.");
  }
}

/// Reads the cached manifest.
///
/// A missing or unreadable one is an empty manifest, which makes the next update fetch
/// everything - the safe direction to be wrong in.
List<ManifestEntry> _readLocalManifest(File file) {
  try {
    return parseManifest(file.readAsStringSync(encoding: utf8));
  } catch (_) {
    return const <ManifestEntry>[];
  }
}

/// Joins a base URL and a file name.
///
/// Not a path join, which on Windows produces a backslash and a URL no server will answer.
String _joinUrl(String baseUrl, String name) {
  final base = baseUrl.endsWith('/') ? baseUrl.substring(0, baseUrl.length - 1) : baseUrl;

  return '$base/${name.replaceAll(r'\', '/')}';
}

/// A manifest name as a local path, whichever separator this platform writes.
String _localPath(String directory, String name) =>
    '$directory${Platform.pathSeparator}${name.replaceAll('/', Platform.pathSeparator)}';

// ---------------------------------------------------------------------------- MD5

const List<int> _md5Shifts = <int>[
  7, 12, 17, 22, 7, 12, 17, 22, 7, 12, 17, 22, 7, 12, 17, 22, //
  5, 9, 14, 20, 5, 9, 14, 20, 5, 9, 14, 20, 5, 9, 14, 20,
  4, 11, 16, 23, 4, 11, 16, 23, 4, 11, 16, 23, 4, 11, 16, 23,
  6, 10, 15, 21, 6, 10, 15, 21, 6, 10, 15, 21, 6, 10, 15, 21,
];

/// The per-round constants, which are floor(abs(sin(i + 1)) * 2^32).
///
/// Computed rather than tabulated. The table is sixty-four magic numbers and the one part
/// of MD5 a transcription error hides in; the definition is this line, and `dart:math` is
/// in the SDK like everything else here.
final List<int> _md5Sines = List<int>.generate(
    64, (i) => (math.sin(i + 1).abs() * 4294967296.0).floor() & 0xFFFFFFFF,
    growable: false);

/// The MD5 of some bytes, in the lower-case hex the manifest carries.
String md5Hex(List<int> input) {
  final message = Uint8List.fromList(input);
  final bitLength = message.length * 8;

  // Padded to a multiple of 64 bytes: a 0x80 byte, zeroes, and the original bit length
  // as a little-endian 64-bit number.
  var padded = message.length + 1;
  padded += (56 - (padded % 64) + 64) % 64;

  final block = Uint8List(padded + 8);
  block.setRange(0, message.length, message);
  block[message.length] = 0x80;

  final view = ByteData.view(block.buffer);
  view.setUint32(block.length - 8, bitLength & 0xFFFFFFFF, Endian.little);
  view.setUint32(block.length - 4, (bitLength ~/ 0x100000000) & 0xFFFFFFFF, Endian.little);

  var a0 = 0x67452301, b0 = 0xefcdab89, c0 = 0x98badcfe, d0 = 0x10325476;

  for (var offset = 0; offset < block.length; offset += 64) {
    final words = List<int>.generate(
        16, (i) => view.getUint32(offset + i * 4, Endian.little),
        growable: false);

    var a = a0, b = b0, c = c0, d = d0;

    for (var i = 0; i < 64; i++) {
      int f;
      int g;

      if (i < 16) {
        f = (b & c) | (~b & d);
        g = i;
      } else if (i < 32) {
        f = (d & b) | (~d & c);
        g = (5 * i + 1) % 16;
      } else if (i < 48) {
        f = b ^ c ^ d;
        g = (3 * i + 5) % 16;
      } else {
        f = c ^ (b | (~d & 0xFFFFFFFF));
        g = (7 * i) % 16;
      }

      f = (f + a + _md5Sines[i] + words[g]) & 0xFFFFFFFF;

      a = d;
      d = c;
      c = b;
      b = (b + _rotateLeft(f, _md5Shifts[i])) & 0xFFFFFFFF;
    }

    a0 = (a0 + a) & 0xFFFFFFFF;
    b0 = (b0 + b) & 0xFFFFFFFF;
    c0 = (c0 + c) & 0xFFFFFFFF;
    d0 = (d0 + d) & 0xFFFFFFFF;
  }

  final digest = Uint8List(16);
  final digestView = ByteData.view(digest.buffer);

  digestView.setUint32(0, a0, Endian.little);
  digestView.setUint32(4, b0, Endian.little);
  digestView.setUint32(8, c0, Endian.little);
  digestView.setUint32(12, d0, Endian.little);

  final hex = StringBuffer();
  for (final byte in digest) {
    hex.write(byte.toRadixString(16).padLeft(2, '0'));
  }

  return hex.toString();
}

int _rotateLeft(int value, int bits) =>
    ((value << bits) | (value >> (32 - bits))) & 0xFFFFFFFF;
