// Conformance harness for the generated Unreal reader.
//
// Reads Vectors.tcb through the generated accessor and prints each row in the canonical form
// described in ../README.md. No parsing here: the generated reader does that.
//
// This one is built against the stubs in ../../unreal-stubs rather than against an engine, which
// is the whole reason it exists. Everything the corpus compares - the varints, the zig-zag, the
// UTF-8, the GUID byte order, the ticks - is the generated code's and the reader's. What the
// stubs supply is storage and formatting.
//
// The GUID is formatted here rather than by FGuid::ToString, on purpose. The engine's default
// ToString is EGuidFormats::Digits, which has no hyphens, and going through a formatter this file
// does not own would put the stub's spelling between the reader and the comparison. Writing the
// four components out in .NET's "D" order keeps the check on the reader's assembly, which is what
// the corpus is here to pin.

#include <cstdint>
#include <cstdlib>
#include <iomanip>
#include <iostream>
#include <sstream>
#include <string>

#include TABBIT_ACCESSOR_HEADER

namespace {

// Round-trip precision, so the printed value is the one that was read.
std::string number(float value) {
  std::ostringstream out;
  out << std::setprecision(9) << value;
  return out.str();
}

std::string number(double value) {
  std::ostringstream out;
  out << std::setprecision(17) << value;
  return out.str();
}

std::string quote(const std::string& value) {
  std::ostringstream out;
  out << '"';

  for (unsigned char c : value) {
    if (c == '"') {
      out << "\\\"";
    } else if (c == '\\') {
      out << "\\\\";
    } else if (c == '\n') {
      out << "\\n";
    } else if (c == '\r') {
      out << "\\r";
    } else if (c == '\t') {
      out << "\\t";
    } else if (c < 0x20) {
      out << "\\u" << std::hex << std::setw(4) << std::setfill('0')
          << static_cast<int>(c) << std::dec;
    } else {
      // Bytes above 0x7f pass through: the source was UTF-8 and so is the output.
      out << static_cast<char>(c);
    }
  }

  out << '"';
  return out.str();
}

/// The four components in .NET's "D" spelling: 8-4-4-4-12, lower case.
std::string guid(const FGuid& value) {
  std::ostringstream out;
  out << std::hex << std::nouppercase << std::setfill('0');

  out << std::setw(8) << value.A << '-';
  out << std::setw(4) << (value.B >> 16) << '-';
  out << std::setw(4) << (value.B & 0xFFFF) << '-';
  out << std::setw(4) << (value.C >> 16) << '-';
  out << std::setw(4) << (value.C & 0xFFFF);
  out << std::setw(8) << value.D;

  return out.str();
}

}  // namespace

int main(int argc, char** argv) {
  if (argc < 2) {
    std::cerr << "usage: conformance-unreal <binary-directory>\n";
    return 1;
  }

  // A narrow argv into the engine's string type, which is what the accessor takes.
  FString base_path;
  {
    std::string narrow(argv[1]);
    std::u16string wide(narrow.begin(), narrow.end());
    base_path = FString(static_cast<int32>(wide.size()), wide.c_str());
  }

  // The corpus is signed, so the key goes in before the first read - which is the whole of
  // what a consuming project does about the MAC. Without it the files would still load, and
  // nothing here would notice: the check is the reader's, and it needs the key to run.
  if (const char* text = std::getenv("TABBIT_TEST_TCB_MAC_KEY")) {
    const std::string hex(text);

    if (hex.size() == 64) {
      ConformanceData::MacKey.SetNum(32);

      for (int32 at = 0; at < 32; ++at) {
        ConformanceData::MacKey[at] = static_cast<uint8>(
            std::stoul(hex.substr(static_cast<std::size_t>(at) * 2, 2), nullptr, 16));
      }
    }
  }

  if (!ConformanceData::ReadAll(base_path)) {
    std::cerr << "the generated accessor could not read the corpus\n";
    return 1;
  }

  std::ostringstream json;
  json << '[';

  const TArray<FVectorsRow>& records = ConformanceData::Vectors().Records();

  for (int32 i = 0; i < records.Num(); ++i) {
    const FVectorsRow& r = records[i];

    if (i > 0) json << ',';

    json << '{';
    json << "\"index\":" << r.Index << ',';
    json << "\"int_val\":" << r.IntVal << ',';
    json << "\"big_val\":\"" << r.BigVal << "\",";
    json << "\"float_val\":" << number(r.FloatVal) << ',';
    json << "\"double_val\":" << number(r.DoubleVal) << ',';
    json << "\"text\":" << quote(r.Text.ToUtf8()) << ',';
    json << "\"flag\":" << (r.bFlag ? "true" : "false") << ',';
    json << "\"when\":\"" << r.When.GetTicks() << "\",";
    json << "\"span\":\"" << r.Span.GetTicks() << "\",";
    json << "\"uid\":\"" << guid(r.Uid) << "\",";
    json << "\"label\":" << static_cast<std::int32_t>(r.Label) << ',';

    json << "\"ints\":[";
    for (int32 k = 0; k < r.Ints.Num(); ++k)
      json << (k > 0 ? "," : "") << r.Ints[k];
    json << "],";

    json << "\"strs\":[";
    for (int32 k = 0; k < r.Strs.Num(); ++k)
      json << (k > 0 ? "," : "") << quote(r.Strs[k].ToUtf8());
    json << "],";

    // The two array forms whose element read is not the scalar one in a loop.
    json << "\"labels\":[";
    for (int32 k = 0; k < r.Labels.Num(); ++k)
      json << (k > 0 ? "," : "") << static_cast<std::int32_t>(r.Labels[k]);
    json << "],";

    json << "\"uids\":[";
    for (int32 k = 0; k < r.Uids.Num(); ++k)
      json << (k > 0 ? "," : "") << '"' << guid(r.Uids[k]) << '"';
    json << ']';

    // The reference indices, which is what the exporter writes for a foreign field.
    json << ",\"owner\":" << r.Owner;
    json << ",\"tier\":" << r.Tier;

    // And one reference per element, printed as the stored index each came in as.
    json << ",\"owners\":[";
    for (int32 k = 0; k < r.Owners.Num(); ++k)
        json << (k > 0 ? "," : "") << r.Owners[k];
    json << "]";

    // The three the v104 encodings win on.
    json << ",\"count\":" << number(r.Count);
    json << ",\"route\":" << quote(r.Route.ToUtf8());
    json << ",\"zone\":" << quote(r.Zone.ToUtf8());

    json << '}';
  }

  json << ']';

  std::cout << json.str();
  return 0;
}
