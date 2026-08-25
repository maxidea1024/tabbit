// Round-trip check for the C++ generator.
//
// Compiles the generated header, loads the .tcb files the binary exporter wrote,
// and prints what it read as JSON on stdout. The test harness compares that against
// the JSON exporter's output for the same workbook.
//
// The point is to catch the two things a golden-file comparison cannot: that the
// generated header is valid C++ at all, and that the C++ reader agrees with the C#
// writer about the byte format. Those are separate programs that have to stay in
// lockstep, and nothing else in the suite checks the second one.

#include <cstdio>
#include <iostream>
#include <string>
#include <vector>

// Set by the build so the same source can be pointed at any scenario's header.
#ifndef TABBIT_ACCESSOR_HEADER
#define TABBIT_ACCESSOR_HEADER "CoreAccessor.h"
#endif

#include TABBIT_ACCESSOR_HEADER

namespace {

// Minimal JSON emission. Deliberately not a library: the comparison is done by the
// test in C#, so this only has to be unambiguous, not general.

std::string quote(const std::string& value) {
  std::string out = "\"";

  for (const char c : value) {
    switch (c) {
      case '"': out += "\\\""; break;
      case '\\': out += "\\\\"; break;
      case '\n': out += "\\n"; break;
      case '\r': out += "\\r"; break;
      case '\t': out += "\\t"; break;
      default:
        if (static_cast<unsigned char>(c) < 0x20) {
          char buffer[8];
          std::snprintf(buffer, sizeof(buffer), "\\u%04x", c);
          out += buffer;
        } else {
          out += c;  // UTF-8 bytes pass through unchanged.
        }
    }
  }

  return out + "\"";
}

template <typename T>
std::string join(const std::vector<T>& values, std::string (*render)(const T&)) {
  std::string out = "[";

  for (std::size_t i = 0; i < values.size(); ++i) {
    if (i > 0) out += ",";
    out += render(values[i]);
  }

  return out + "]";
}

// One renderer per cell type this harness knows how to print. Which of them a run needs
// depends on the scenario it was pointed at, and the build is -Wextra -Werror - so the
// ones a given scenario has no column for are not a mistake.
[[maybe_unused]] std::string render_int(const std::int32_t& v) { return std::to_string(v); }
[[maybe_unused]] std::string render_string(const std::string& v) { return quote(v); }
[[maybe_unused]] std::string render_float(const float& v) { return std::to_string(static_cast<double>(v)); }

}  // namespace

int main(int argc, char** argv) {
  if (argc < 2) {
    std::cerr << "usage: cpp-check <binary-table-directory>\n";
    return 2;
  }

  try {
    tabbit_fixtures::core::CoreAccessor tables;
    tables.read_all(argv[1]);

    std::cout << "{";

    // Every primitive type, so a disagreement about any one of them shows up.
    {
      const auto& records = tables.test_field_types().records();
      std::cout << quote("TestFieldTypes") << ":[";
      for (std::size_t i = 0; i < records.size(); ++i) {
        const auto& r = records[i];
        if (i > 0) std::cout << ",";
        std::cout << "{"
                  << quote("index") << ":" << r.index << ","
                  << quote("stringField") << ":" << quote(r.string_field) << ","
                  << quote("boolField") << ":" << (r.bool_field ? "true" : "false") << ","
                  << quote("intField") << ":" << r.int_field << ","
                  << quote("bigIntField") << ":" << r.big_int_field << ","
                  << quote("uuidField") << ":" << quote(r.uuid_field.to_string()) << ","
                  << quote("valueTypeField") << ":" << static_cast<std::int32_t>(r.value_type_field)
                  << "}";
      }
      std::cout << "],";
    }

    // Delimited arrays, whose length is on the wire, next to a serial field whose
    // length is not.
    {
      const auto& records = tables.array_types().records();
      std::cout << quote("ArrayTypes") << ":[";
      for (std::size_t i = 0; i < records.size(); ++i) {
        const auto& r = records[i];
        if (i > 0) std::cout << ",";

        std::vector<std::int32_t> grades;
        for (const auto g : r.grades) grades.push_back(static_cast<std::int32_t>(g));

        std::cout << "{"
                  << quote("index") << ":" << r.index << ","
                  << quote("tags") << ":" << join(r.tags, render_string) << ","
                  << quote("costs") << ":" << join(r.costs, render_int) << ","
                  << quote("grades") << ":" << join(grades, render_int) << ","
                  << quote("slot") << ":" << join(r.slot, render_int)
                  << "}";
      }
      std::cout << "],";
    }

    // Cross-table references, resolved to pointers after every table is loaded.
    {
      const auto& records = tables.item().records();
      std::cout << quote("Item") << ":[";
      for (std::size_t i = 0; i < records.size(); ++i) {
        const auto& r = records[i];
        if (i > 0) std::cout << ",";
        std::cout << "{"
                  << quote("index") << ":" << r.index << ","
                  << quote("name") << ":" << quote(r.name) << ","
                  << quote("categoryName") << ":"
                  << quote(r.item_category_by_category_id != nullptr
                            ? r.item_category_by_category_id->name : std::string("<unresolved>"))
                  << "}";
      }
      std::cout << "]";
    }

    std::cout << "}" << std::endl;
    return 0;
  } catch (const std::exception& ex) {
    std::cerr << "failed: " << ex.what() << "\n";
    return 1;
  }
}
