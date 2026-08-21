using System;
using System.IO;
using CommandLine;
using Tabbit.Models.Raw;
using Tabbit.Importers;
using Tabbit.Cooking;
using Tabbit.History;
using Tabbit.Exporters;
using Tabbit.CodeGeneration;
using Tabbit.Recipe;
using Serilog;
using System.Diagnostics;
using Tabbit.Helpers;
using Tabbit.Extensions;
using System.Collections.Generic;
using Tabbit.Targets;
using Tabbit.Sources;
using Tabbit.Validation;

namespace Tabbit;

class Program
{
    static int Main(string[] args)
    {
        //string summaryFilename = Path.Combine(Environment.GetFolderPath(System.Environment.SpecialFolder.Personal), ".tabbit/tabbit.summary.json"); ;

        if (args.Length == 1 && args[0].StartsWith("@"))
        {
            var argFile = args[0][1..];
            if (File.Exists(argFile))
            {
                args = File.ReadAllLines(argFile);
            }
            else
            {
                Console.WriteLine($"File not found: {argFile}");
                return 1;
            }
        }

        var parser = new Parser(recipe => recipe.HelpWriter = Console.Out);
        if (args.Length == 0)
        {
            parser.ParseArguments<Options>(new[] { "--help" });
            return 1;
        }

        Options? options = null;
        parser.ParseArguments<Options>(args)
            .WithParsed(r => { options = r; });

        // WithParsed only fires on success, so a rejected argument leaves `options`
        // null. Every path below dereferences it, so bail out here instead.
        // CommandLineParser has already written the error and the help text.
        if (options is null)
            return 1;

        SetupLogging(options.Verbose, options.Silent);

        // Serilog's file sink buffers, so the last writes are lost unless the
        // logger is closed. Every exit below runs through this.
        try
        {
            return Run(parser, options);
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }

    /// <summary>
    /// Writes a new encryption key, and says what to do with it.
    /// </summary>
    /// <remarks>
    /// The key goes to standard output on its own line and nothing else does, so that piping
    /// this into a secret store gets the key and not a sentence about it. What is written
    /// beside it goes to standard error for the same reason.
    ///
    /// An existing file is left alone. Overwriting one would leave every file already
    /// exported under the old key unreadable, and the only symptom would be a client that
    /// refuses to load its data with no indication that a key was replaced.
    /// </remarks>
    private static int WriteNewEncryptionKey(string? filename)
    {
        string key = Exporters.TcbEnvelope.NewKey();

        if (string.IsNullOrEmpty(filename))
        {
            Console.WriteLine(key);

            Console.Error.WriteLine(
                "Put this in the environment variable the recipe's `EncryptionKeyVariable` names,"
                + " or in the file its `EncryptionKeyFile` points at.");

            return 0;
        }

        string path = Path.GetFullPath(filename);

        if (File.Exists(path))
        {
            Console.Error.WriteLine(
                $"{path} already exists. Every file exported with the key in it would become"
                + " unreadable if it were replaced, so it is left alone - delete it first if"
                + " that is what you mean to do.");

            return 1;
        }

        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        File.WriteAllText(path, key + Environment.NewLine);

        Console.Error.WriteLine($"Wrote a new encryption key to {path}");
        Console.Error.WriteLine("Keep it out of the repository the recipe is in.");

        return 0;
    }

    private static int Run(Parser parser, Options options)
    {
        if (!string.IsNullOrEmpty(options.NewRecipeFilename))
        {
            // Its own try, because this runs before the conversion's error handling is set
            // up - and naming a template that does not exist is the most likely way to get
            // this wrong, so it has to answer with the list rather than a stack trace.
            try
            {
                if (string.IsNullOrEmpty(options.RecipeTemplate))
                    RecipeSkeleton.WriteToFile(options.NewRecipeFilename);
                else
                    RecipeSkeleton.WriteTemplateToFile(options.NewRecipeFilename, options.RecipeTemplate);
            }
            catch (TabbitException ex)
            {
                Console.Error.WriteLine(ex.Message);
                return 1;
            }

            Console.WriteLine($"Wrote a starting recipe to {Path.GetFullPath(options.NewRecipeFilename)}");
            return 0;
        }

        // Before the recipe is required, because making a key has nothing to do with a
        // conversion - and the first thing anyone setting encryption up needs is a key to
        // put in the environment variable the recipe will name.
        if (options.NewEncryptionKey)
        {
            try
            {
                return WriteNewEncryptionKey(options.Out);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.Message);
                return 1;
            }
        }

        RecipeModel? recipe = null;
        if (!string.IsNullOrEmpty(options.RecipeFilename))
        {
            try
            {
                // Before the recipe is read, because the recipe may name it: `--env` is
                // what `${TABBIT_ENV}` resolves to, so the run is labelled and pointed at
                // its sheets by the same word.
                RunEnvironment.Establish(options);

                recipe = RecipeModel.LoadFromFile(options.RecipeFilename);
            }
            catch (Exception ex)
            {
                // Through the same reporter every other failure uses. It printed only
                // ex.Message here, so a recipe that failed to load lost the cell location
                // and the collected details that the reporter exists to show - and lost
                // them to standard output rather than to the log, where the rest of a
                // failing run goes.
                LogException(options, ex);
                return 1;
            }
        }
        else
        {
            parser.ParseArguments<Options>(new[] { "--help" });
            return 1;
        }

        // Every path to here loaded a recipe: the `else` above returns, and so does the
        // `catch`. The compiler cannot follow that through the `try`, so this states it -
        // and it is a real guard rather than a cast, so a future edit that lets one through
        // stops here instead of at the first member it reads.
        if (recipe is null)
            return 1;

        // Scaffolding, which needs the recipe for the folder and nothing else. Before the
        // conversion because it is not one: no sheet is read and no output is produced.
        if (!string.IsNullOrEmpty(options.NewValidator))
        {
            try
            {
                string written = Validation.RuleScaffold.WriteNewValidator(
                    recipe.Validation, options.NewValidator);

                Console.WriteLine($"Wrote a starting validation rule to {written}");
                return 0;
            }
            catch (Exception ex)
            {
                LogException(options, ex);
                return 1;
            }
        }

        // Listing the rules is not a conversion either: the folders and the attributes on the
        // files are all it reads, and no sheet is opened.
        if (options.ListValidators)
        {
            try
            {
                var folders = Validation.RuleFolders.Discover(recipe.Validation);

                Console.WriteLine(folders is null
                    ? "This recipe has no `Validation.Path`, so there are no rules to list."
                    : Validation.RuleScaffold.DescribeOrder(folders));

                return 0;
            }
            catch (Exception ex)
            {
                LogException(options, ex);
                return 1;
            }
        }

        // Reading the history is not a conversion: no sources are imported, nothing is
        // written to the output tree, and the answer goes to standard output. The recipe
        // is still needed, because that is where the history's address is.
        if (options.History || options.Stats || options.Serve || options.Prune)
        {
            try
            {
                if (options.Serve)
                    return HistoryServer.Run(options, recipe);

                if (options.Prune)
                    return HistoryCommand.RunPrune(options, recipe);

                return options.History
                    ? HistoryCommand.RunHistory(options, recipe)
                    : HistoryCommand.RunStats(options, recipe);
            }
            catch (Exception ex)
            {
                LogException(options, ex);
                return 1;
            }
        }

        {
            Log.Information($"Start working with recipe `{Path.GetFullPath(options.RecipeFilename)}`");

            var stopWatch = new Stopwatch();

            stopWatch.Start();
            int rc = Process(options, recipe);
            stopWatch.Stop();

            if (rc == 0)
            {
                if (!options.Silent)
                {
                    Log.Information($"All work is done successfuly. Total time spent is {stopWatch.ElapsedMilliseconds} ms.");
                    //Log.Information($"  Take a look at the `{summaryFilename}` for details on the results.");
                }
            }

            return rc;
        }
    }

    private static int Process(Options options, RecipeModel recipeModel)
    {
        try
        {
            // Read before any work starts, and discarded: the consumers below take it
            // from the options themselves. Parsing it here is what turns a misspelled
            // --target-side into an immediate error rather than one reported after
            // every workbook has been read.
            CommandLineTargetSide.Of(options);

            // Stamped onto the recipe before anything reads it, so the zone every dated cell
            // is read in is settled in one place. A zone that names no place stops the run
            // here, with no workbook opened.
            CommandLineTimeZone.Apply(options, recipeModel);

            // Same reason: a misspelled --commit-date should be reported now rather
            // than after every workbook has been read. Working out which commit this
            // is spawns git, so that part waits until a target asks for it.
            CommitInfo.ValidateOptions(options);

            // Read now, before anything is imported, so a validation folder that does not
            // exist is reported with no work done. Null when the recipe asks for none.
            var validation = ValidationPipeline.Create(options, recipeModel);

            // What can be answered before a workbook is opened: file names, settings,
            // whatever a project's own conventions require of its sources.
            validation?.RunPre();


            // Imports

            // Every source the recipe lists, into one raw model: a project may spread
            // its tables across workbooks and Google Sheets documents and they cook
            // together. Which sources exist is discovered by attribute, so adding one
            // touches only the file that defines it.
            RawModel rawModel = new RawModel();

            SourceRegistry.ImportAll(options, recipeModel, rawModel);


            // Cooking

            var cooker = new ModelCooker();
            var model = cooker.Cook(options, recipeModel, rawModel);

            // Before validation on purpose. What this writes is where the tables are, and a
            // workbook with a broken value still has its tables in the same places - so a
            // merge waiting on this answer is not held up by a problem it is not about.
            if (!string.IsNullOrEmpty(options.DumpSchema))
            {
                SheetSchema.Write(
                    model, options.DumpSchema,
                    typeof(SheetSchema).Assembly.GetName().Version?.ToString() ?? "");
                return 0;
            }


            // Validation

            // Ahead of every target rather than after them. The file targets stage their
            // output and could be rolled back, but each database target swaps its shadow in
            // as it runs - so validating afterwards would report a failure against data that
            // has already changed. Every output is a deterministic projection of the model,
            // so checking the model is checking the output, and a failed run leaves no trace
            // in a file or in a database.
            validation?.RunPost(model);

            if (options.ValidateOnly)
            {
                Log.Information("Validation passed. Stopping before any output, as --validate-only asks.");
                return 0;
            }


            // Output

            // Every export and code-generation target the recipe asks for, in a fixed
            // order. Which targets exist is discovered by attribute, so adding one
            // touches only the file that defines it - this used to be a run of ten
            // near-identical `if (recipe.X.Y.Count > 0)` blocks, and the validation
            // pass had to name the same sections a second time.
            //
            // The database targets differ from the file ones in when their output
            // becomes visible. File targets stage their work and commit it below,
            // while each database target loads into shadow storage and swaps it in as
            // it goes. Atomicity is per store either way: files and four databases
            // cannot share one transaction without a distributed coordinator.

            TargetRegistry.RunAll(options, recipeModel, model);

            Log.Information("Now that we have completed all the work, we are copying the generated staging files to the destination folder.");

            try
            {
                StagingFiles.CommitFiles((filename, stagedFilename) =>
                {
                    Log.Debug($"Commit staged file `{filename}`");
                });
            }
            catch (Exception ex)
            {
                // Delete all files created in the staging area.
                StagingFiles.Rollback();

                LogException(options, ex,
                    "While moving the artifact file to the actual target path, We got the below error. " +
                    "This would have caused problems with the final result. " +
                    "Please return to the previous state with version control such as git or svn."
                );

                return 1;
            }
        }
        catch (Exception ex)
        {
            LogException(options, ex);

            return 1;
        }

        return 0;
    }

    private static void LogException(Options options, Exception ex, string subject = "")
    {
        Log.Fatal(ex.Message);

        if (ex is TabbitException tabbitEx)
        {
            if (tabbitEx.Location is not null)
                Log.Fatal($"   at {tabbitEx.Location}");

            if (tabbitEx.Details is not null && tabbitEx.Details.Count > 0)
            {
                // Header printed once, ahead of the list. It used to be inside the
                // loop, so it was repeated before every single entry.
                Log.Fatal("");
                Log.Fatal("Details:");

                for (int detailIndex = 0; detailIndex < tabbitEx.Details.Count; detailIndex++)
                {
                    var detail = tabbitEx.Details[detailIndex];

                    Log.Fatal($"  [{detailIndex + 1,3}] {detail.Message}");
                    if (detail.Location is not null)
                        Log.Fatal($"        at {detail.Location}");
                }
            }
        }

        if (options.Debugging && ex.StackTrace is not null)
        {
            Log.Fatal("");
            Log.Fatal("Callstack:");
            Log.Fatal(ex.StackTrace);
        }
    }

    private static void SetupLogging(bool verbose, bool silent)
    {
        Serilog.Events.LogEventLevel loggingLevel = Serilog.Events.LogEventLevel.Information;

        if (silent)
            loggingLevel = Serilog.Events.LogEventLevel.Error;
        else if (verbose)
            loggingLevel = Serilog.Events.LogEventLevel.Debug;

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Console(outputTemplate: "{Message:lj}{NewLine}{Exception}",
                            restrictedToMinimumLevel: loggingLevel)
            .WriteTo.File("logs/tabbit.log", rollingInterval: RollingInterval.Day)
            .CreateLogger();
    }
}
