// Drives the shipped TypeScript updater for the test suite.
//
// The updater is a fixed runtime file - it reads the manifest and knows nothing of the
// schema - so what runs here is what a consumer gets, compiled by tsc and run by node
// against a server the test starts on a real port.
//
//   node out/main.js <base-url> <cache-directory>
//
// Prints the result object as JSON. A failed update is an outcome to assert, not a
// failure of this program, so it exits zero either way.

import { update } from './tabbit/updater'

async function main(): Promise<void> {
  const [baseUrl, cacheDirectory] = process.argv.slice(2)

  // A millisecond of backoff instead of half a second: the retry is being asserted, not
  // waited for.
  const result = await update(baseUrl, cacheDirectory, { retryDelayMs: 1 })

  console.log(JSON.stringify(result))
}

main()
