// Read-back check for a sealed binary export.
//
// Compiles against the generated accessor and loads the .tcb files the binary exporter
// wrote with a key. What it is here to demonstrate is the whole of what a consuming project
// has to do about encryption: set `EncryptedAccessor.EncryptionKey` once, before the first read, and
// call the same `ReadAllAsync` an unencrypted project calls.
//
// A separate harness from `cs-check` because that one names the `core` fixture's tables, and
// because two test classes building one project with different `GeneratedDir` values race.
//
// The keys arrive as arguments rather than being written here, which is also how they are
// meant to reach a real client - from wherever that project keeps secrets, at start-up.
// Without them the load is expected to fail, and the harness prints why, so the test can
// assert that the reader names the cause itself.
//
// Two keys, because the two layers are independent: the first seals the file and the second
// says it is the file that was exported. A project can use either, both or neither, and the
// only difference in this code is which of the two statics is set.
//
// spec/tcb-v104-composed-encodings.md section 4 · spec/tcb-mac-and-signature.md.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

using Tabbit.Fixtures.Encrypted;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        if (args.Length < 1)
        {
            Console.Error.WriteLine(
                "usage: cs-check-encrypted <binary-table-directory> [hex-key] [hex-mac-key]");
            return 2;
        }

        if (args.Length > 1 && args[1].Length > 0)
            EncryptedAccessor.EncryptionKey = Convert.FromHexString(args[1]);

        if (args.Length > 2 && args[2].Length > 0)
            EncryptedAccessor.MacKey = Convert.FromHexString(args[2]);

        try
        {
            await EncryptedAccessor.ReadAllAsync(args[0]);

            var rows = EncryptedAccessor.Animation.Records.Select(record => new Dictionary<string, object>
            {
                ["index"] = record.Index,
                ["blend"] = record.Blend.ToString("R", CultureInfo.InvariantCulture),
                ["slot"] = record.Slot,
            }).ToList();

            Console.WriteLine(JsonSerializer.Serialize(rows));
            return 0;
        }
        catch (Exception error)
        {
            // The message alone, not the stack: what the test is asking is whether the
            // reader said why, and a stack would let any failure satisfy that.
            Console.Error.WriteLine(error.Message);
            return 1;
        }
    }
}
