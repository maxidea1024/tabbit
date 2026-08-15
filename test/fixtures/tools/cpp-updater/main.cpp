// Drives one update and prints what it did, for the C# test to assert against.
//
// The updater under test is the shipped one - lib/cpp/tabbit/updater.h - compiled
// beside this file exactly as a consumer's build would compile it, with the same one
// link flag: -lcurl.
//
// The MD5 vectors are printed too. They are the published ones, and they are the only
// reading of the sixty-four constants in that file that counts.

#include <cstdio>
#include <iostream>
#include <string>
#include <vector>

#include "tabbit/updater.h"

namespace {

std::vector<std::uint8_t> bytes_of(const std::string& text) {
  return std::vector<std::uint8_t>(text.begin(), text.end());
}

/// A JSON string, or null. Enough escaping for a path and a message.
std::string quote(const std::string& value) {
  if (value.empty()) return "null";

  std::string quoted = "\"";

  for (char c : value) {
    switch (c) {
      case '"': quoted += "\\\""; break;
      case '\\': quoted += "\\\\"; break;
      case '\n': quoted += "\\n"; break;
      case '\r': quoted += "\\r"; break;
      case '\t': quoted += "\\t"; break;
      default:
        if (static_cast<unsigned char>(c) < 0x20) {
          char escape[8];
          std::snprintf(escape, sizeof escape, "\\u%04x",
                        static_cast<unsigned>(static_cast<unsigned char>(c)));
          quoted += escape;
        } else {
          quoted += c;
        }
    }
  }

  return quoted + "\"";
}

}  // namespace

int main(int argc, char** argv) {
  if (argc < 3) {
    std::cerr << "usage: cpp-updater <base-url> <cache-directory>\n";
    return 2;
  }

  std::cout << "{\"md5abc\":\"" << tabbit::md5_hex(bytes_of("abc"))
            << "\",\"md5empty\":\"" << tabbit::md5_hex(bytes_of(""))
            << "\",\"md5fox\":\""
            << tabbit::md5_hex(bytes_of("The quick brown fox jumps over the lazy dog"))
            << "\"}\n";

  tabbit::UpdateOptions options;

  // Short, because the retry test would otherwise spend its time asleep.
  options.retry_delay = std::chrono::milliseconds(50);
  options.log = [](const std::string& message) { std::cerr << message << "\n"; };

  const tabbit::UpdateResult result = tabbit::update(argv[1], argv[2], options);

  std::cout << "{\"succeeded\":" << (result.succeeded ? "true" : "false")
            << ",\"error\":" << quote(result.error)
            << ",\"upToDate\":" << (result.up_to_date ? "true" : "false")
            << ",\"downloadedCount\":" << result.downloaded_count
            << ",\"downloadedBytes\":" << result.downloaded_bytes
            << ",\"deletedCount\":" << result.deleted_count
            << ",\"localPath\":" << quote(result.local_path.string())
            << "}\n";

  return 0;
}
