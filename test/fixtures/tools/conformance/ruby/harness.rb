# Conformance harness for the generated Ruby reader.
#
# Reads Vectors.tcb through the generated accessor and prints each row in the canonical
# form described in ../README.md. No parsing here: the generated reader does that.

require_relative 'tables'

def quote(value)
  quoted = +'"'

  value.each_char do |c|
    case c
    when '"' then quoted << '\\"'
    when '\\' then quoted << '\\\\'
    when "\n" then quoted << '\\n'
    when "\r" then quoted << '\\r'
    when "\t" then quoted << '\\t'
    else
      quoted << (c.ord < 0x20 ? format('\\u%04x', c.ord) : c)
    end
  end

  quoted << '"'
end

if ARGV.empty?
  warn 'usage: harness.rb <binary-directory>'
  exit 1
end

# The corpus is signed, so the key goes in before the first read - which is the whole of
# what a consuming project does about the MAC. Without it the files would still load, and
# nothing here would notice: the check is the reader's, and it needs the key to run.
mac_key = ENV['TABBIT_TEST_TCB_MAC_KEY']
Conformance::Tables.mac_key = [mac_key].pack('H*') if mac_key && !mac_key.empty?

tables = Conformance::Tables.new
tables.read_all(ARGV[0])

json = +'['

tables.vectors.records.each_with_index do |r, position|
  json << ',' if position.positive?

  json << '{'
  json << '"index":' << r.index.to_s << ','
  json << '"intVal":' << r.int_val.to_s << ','

  # A string, because JSON's single numeric type would round anything past 2^53.
  json << '"bigVal":"' << r.big_val.to_s << '",'

  json << '"floatVal":' << r.float_val.to_s << ','
  json << '"doubleVal":' << r.double_val.to_s << ','
  json << '"text":' << quote(r.text) << ','
  json << '"flag":' << r.flag.to_s << ','

  # Ticks, which is what the generated fields hold.
  json << '"when":"' << r.when_.to_s << '",'
  json << '"span":"' << r.span.to_s << '",'

  json << '"uid":"' << r.uid.to_s << '",'
  json << '"label":' << r.label.to_s << ','

  json << '"ints":[' << r.ints.map(&:to_s).join(',') << '],'
  json << '"strs":[' << r.strs.map { |value| quote(value) }.join(',') << ']'
  # The two array forms whose element read is not the scalar one in a loop.
  json << ',"labels":[' << r.labels.map(&:to_s).join(',') << ']'
  json << ',"uids":[' << r.uids.map { |value| quote(value.to_s) }.join(',') << ']'
  # The reference indices, which is what the exporter writes for a foreign field.
  json << ',"owner":' << r.owner.to_s
  json << ',"tier":' << r.tier_index.to_s
  # And one reference per element, printed as the stored index each came in as.
  json << ',"owners":[' << r.owners.map(&:to_s).join(',') << ']'
  # The three the v104 encodings win on.
  json << ',"count":' << r.count.to_s
  json << ',"route":' << quote(r.route)
  json << ',"zone":' << quote(r.zone)

  json << '}'
end

json << ']'

# Written as bytes: Ruby would otherwise transcode to the default external encoding, which
# on Windows is a legacy codepage and would mangle every non-ASCII value in the corpus.
$stdout.binmode
$stdout.write(json.encode(Encoding::UTF_8))
$stdout.flush
