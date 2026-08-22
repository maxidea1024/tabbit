using System.Collections.Generic;
using System.IO;
using Serilog;
using Tabbit.Caching;
using Tabbit.Helpers;
using Tabbit.Messages;
using Tabbit.Models.Raw;
using Tabbit.Recipe;

namespace Tabbit.Schema;

/// <summary>
/// Reads the schema files a recipe points at, and does not interpret them.
/// </summary>
/// <remarks>
/// **Reading and parsing are separated on purpose.** What is read is an input of the build,
/// which is this class and the ledger's business; what it says is a question about the model,
/// answered where every declaration and every sheet are in hand at once. So the text arrives
/// in <see cref="RawModel"/> beside the cells, which is what that class is - everything the
/// run read, before any of it means anything.
///
/// **Every file read is recorded in the cache's ledger, and so is the listing.** The second
/// is not the same fact as the first: adding a declaration file changes no existing file, so
/// without the listing a build whose types grew would hit the cache and hand back the
/// previous run's output. This is the reasoning <see cref="Importers.XlsxImporter"/> already
/// writes down for workbooks, and it holds here for the same reason.
///
/// notes/struct-dsl-design.md section 7.4.
/// </remarks>
public static class SchemaFiles
{
    private static ILogger Log => LogCategory.Importing;

    /// <summary>
    /// Reads every schema file the recipe names, in a fixed order.
    /// </summary>
    /// <remarks>
    /// The order is the recipe's entries and then each directory's own ordering, fixed rather
    /// than the filesystem's - the same reason the workbook walk fixes it. What a declaration
    /// means does not depend on the order it was read in; what a report listing them says
    /// does.
    /// </remarks>
    /// <exception cref="TabbitException">
    /// A directory the recipe names and that is not there. Thrown rather than collected,
    /// because it is settled before a single file has been opened and everything after it
    /// would be a report about declarations this run has not got.
    /// </exception>
    public static List<RawSchemaFile> ReadAll(RecipeModel recipeModel, InputLedger inputs)
    {
        var files = new List<RawSchemaFile>();

        foreach (var entry in recipeModel.Schemas)
        {
            if (string.IsNullOrWhiteSpace(entry.Path))
                continue;

            if (!Directory.Exists(entry.Path))
            {
                throw new TabbitException(null,
                    Message.Of(SchemaMessages.PathMissing, ("Path", entry.Path)));
            }

            var extensions = SourceFiles.Extensions(entry.FileExtensionPatterns);
            var candidates = new List<(string Path, string Name)>(
                SourceFiles.Candidates(entry.Path, extensions));

            inputs.Listed(
                entry.Path,
                entry.FileExtensionPatterns,
                candidates.ConvertAll(candidate => candidate.Name));

            foreach (var (path, name) in candidates)
            {
                inputs.Read(path);

                Log.Debug($"Reading schema file `{name}`.");

                files.Add(new RawSchemaFile
                {
                    // Named the way somebody looking at that directory would rather than by
                    // whatever absolute path this run was given. The name goes into every
                    // report about the file, and two machines should write the same one.
                    Name = name.Replace('\\', '/'),
                    Text = File.ReadAllText(path),
                });
            }
        }

        if (files.Count > 0)
            Log.Information($"Read {files.Count} schema file(s).");

        return files;
    }
}
