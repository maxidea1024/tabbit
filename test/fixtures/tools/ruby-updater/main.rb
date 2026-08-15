# Drives one update and prints what it did, for the C# test to assert against.
#
# The updater under test is the shipped one - lib/ruby/tabbit/updater.rb - copied in
# beside this file and required exactly as a consumer would require it.

require 'json'
require_relative 'tabbit/updater'

base_url = ARGV[0]
cache = ARGV[1]

if base_url.nil? || cache.nil?
  warn 'usage: main.rb <base-url> <cache-directory>'
  exit 2
end

options = Tabbit::UpdateOptions.new(
  # Short, because the retry test would otherwise spend its time asleep.
  retry_delay: 0.05,
  log: ->(message) { warn(message) }
)

result = Tabbit.update(base_url, cache, options)

puts JSON.generate(
  succeeded: result.succeeded,
  error: result.error,
  upToDate: result.up_to_date,
  downloadedCount: result.downloaded_count,
  downloadedBytes: result.downloaded_bytes,
  deletedCount: result.deleted_count,
  localPath: result.local_path
)

exit(result.succeeded ? 0 : 0)
