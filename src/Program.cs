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
using Serilog.Sinks.SystemConsole.Themes;
using System.Diagnostics;
using Tabbit.Helpers;
using Tabbit.Extensions;
using System.Collections.Generic;
using Tabbit.Targets;
using Tabbit.Sources;
using Tabbit.Validation;
using Tabbit.Caching;
using Newtonsoft.Json.Linq;

namespace Tabbit;

class Program
{
    static int Main(string[] args)
    {
        //string summaryFilename = Path.Combine(Environment.GetFolderPath(System.Environment.SpecialFolder.Personal), ".tabbit/tabbit.summary.json"); ;

        UseUtf8ForOutput();

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

        ChooseMessageLanguage(options.Messages);

        SetupLogging(options.Verbose, options.Silent);

        // Which build this is, before anything it does. A log that reaches us without it
        // starts with a round trip asking, and the answer decides how to read everything
        // under it.
        //
        // Not written when this invocation's standard output is somebody else's input:
        // `--new-encryption-key` puts the key there on its own line so it can be piped into
        // a secret store, and a report with no `--out` puts JSON there. The file log gets
        // the line either way, which is where it is read from anyway.
        if (!OwnsStandardOutput(options))
        {
            Log.Information(ToolVersion.Banner);
            Log.Information(ToolVersion.Runtime);

            // A blank line under it, so the header is a header rather than the first two of
            // a wall of lines. Written to the console directly because a blank line through
            // the logger is not blank - it carries the level and the step, which is a line
            // saying nothing rather than a gap.
            //
            // Not under `--silent`, where the two lines above went nowhere and this would be
            // the only thing printed.
            if (!options.Silent)
                Console.WriteLine();
        }

        // Serilog's file sink buffers, so the last writes are lost unless the
        // logger is closed. Every exit below runs through this.
        try
        {
            return Run(parser, options);
        }
        finally
        {
            SayWhatWasNotTranslated();

            Log.CloseAndFlush();
        }
    }

    /// <summary>
    /// Says how much of this run's reporting fell back to English.
    /// </summary>
    /// <remarks>
    /// The difference between "this tool decided to say that in English" and "nobody has
    /// translated it yet" is invisible on screen, and only the second is worth acting on. One
    /// line at the end, and only when there is something to say.
    ///
    /// Counted rather than listed. A run that met forty untranslated reports would otherwise
    /// end in forty lines about translation rather than about the sheets.
    /// </remarks>
    private static void SayWhatWasNotTranslated()
    {
        var catalog = Messages.MessageCatalog.Current;

        if (catalog.Language == Messages.MessageCatalog.FallbackLanguage)
            return;

        if (catalog.Untranslated > 0)
        {
            Log.Information(
                $"{catalog.Untranslated} report(s) came out in English: `{catalog.Language}` "
                + $"has no text for them yet.");
        }

        // Impossible while the catalog gate passes, so it is worth saying loudly rather than
        // counting quietly: it means an id reached a run that no catalog knows at all.
        if (catalog.Unknown > 0)
        {
            Log.Error(
                $"{catalog.Unknown} report(s) had no text in any catalog and came out as their "
                + $"own id. This is a defect in tabbit.");
        }
    }

    /// <summary>
    /// Whether this invocation's standard output is a payload rather than a transcript.
    /// </summary>
    /// <remarks>
    /// Those runs put one thing on standard output and everything else on standard error, so
    /// that a caller can pipe them. A line about which build is running would be the first
    /// thing that pipe received.
    /// </remarks>
    private static bool OwnsStandardOutput(Options options)
    {
        bool toStandardOutput = string.IsNullOrEmpty(options.Out);

        return (options.NewEncryptionKey && toStandardOutput)
            || ((options.History || options.Stats) && toStandardOutput);
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
        JObject? recipeDocument = null;
        if (!string.IsNullOrEmpty(options.RecipeFilename))
        {
            try
            {
                // Before the recipe is read, because the recipe may name it: `--env` is
                // what `${TABBIT_ENV}` resolves to, so the run is labelled and pointed at
                // its sheets by the same word.
                RunEnvironment.Establish(options);

                recipe = RecipeModel.LoadFromFile(options.RecipeFilename, out recipeDocument);
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
            LogCategory.Loading.Information($"Start working with recipe `{Path.GetFullPath(options.RecipeFilename)}`");

            var stopWatch = new Stopwatch();

            stopWatch.Start();
            int rc = Process(options, recipe, recipeDocument);
            stopWatch.Stop();

            // `NothingToDo` is a success that produced nothing, not a failure - so the run
            // says it finished. Leaving it out would mean that asking for the detailed exit
            // code also silently removes the line saying the run went fine.
            if (rc == ExitCode.Success || rc == ExitCode.NothingToDo)
            {
                if (!options.Silent)
                {
                    Log.Information($"All work is done successfully. Total time spent is {stopWatch.Elapsed.TotalSeconds:0.00} s.");
                    //Log.Information($"  Take a look at the `{summaryFilename}` for details on the results.");
                }
            }

            return rc;
        }
    }

    private static int Process(Options options, RecipeModel recipeModel, JObject? recipeDocument)
    {
        var timings = new RunTimings();

        try
        {
            // Read before any work starts, and discarded: the consumers below take it
            // from the options themselves. Parsing it here is what turns a misspelled
            // --target-side into an immediate error rather than one reported after
            // every workbook has been read.
            CommandLineTargetSide.Of(options);

            // Refused rather than resolved. A run that produces no output cannot be asked to
            // produce its output anyway, so one of the two flags is a mistake - and choosing
            // which one wins would hide the mistake until the run ended with the wrong thing
            // done or not done.
            if (options.ValidateOnly && options.ForceOutput)
            {
                    throw new TabbitException(null,
                        Messages.Message.Of(RunMessages.ValidateOnlyWithForceOutput));
            }

            // Stamped onto the recipe before anything reads it, so the zone every dated cell
            // is read in is settled in one place. A zone that names no place stops the run
            // here, with no workbook opened.
            CommandLineTimeZone.Apply(options, recipeModel);

            // Same reason: a misspelled --commit-date should be reported now rather
            // than after every workbook has been read. Working out which commit this
            // is spawns git, so that part waits until a target asks for it.
            CommitInfo.ValidateOptions(options);

            // Before the rules are compiled and before a workbook is opened, because the
            // point of it is to answer "is there anything to do" without paying for either.
            // What it looks at is file sizes and times, one directory listing per source,
            // and one metadata call per hosted document.
            // Measured like any other step, because on a run that skips everything it is the
            // whole of the run - and what it costs is the number that says whether skipping
            // is worth having. Working out the keys is part of it, so the measure covers
            // both halves.
            BuildCache cache;
            CachePlan plan;

            using (timings.Measure(RunTimings.Phase.Deciding))
            {
                cache = BuildCache.Open(options, recipeModel, recipeDocument);
                plan = cache.Decide();
            }

            if (plan == CachePlan.Nothing)
            {
                // The one thing a run with nothing to do still owes: a generated file that
                // is no longer produced is removed whether or not anything else happened.
                cache.SweepUnchanged();

                timings.Report();

                if (!options.DetailedExitCode)
                    return ExitCode.Success;

                LogCategory.Caching.Information(
                    $"Exiting with {ExitCode.NothingToDo}, as --detailed-exit-code asks. "
                    + "Nothing was produced, and nothing failed.");

                return ExitCode.NothingToDo;
            }

            // Read now, before anything is imported, so a validation folder that does not
            // exist is reported with no work done. Null when the recipe asks for none.
            ValidationPipeline? validation;

            using (timings.Measure(RunTimings.Phase.Rules))
                validation = ValidationPipeline.Create(options, recipeModel);

            // What can be answered before a workbook is opened: file names, settings,
            // whatever a project's own conventions require of its sources.
            using (timings.Measure(RunTimings.Phase.Validating))
                validation?.RunPre();


            // Imports

            // Every source the recipe lists, into one raw model: a project may spread
            // its tables across workbooks and Google Sheets documents and they cook
            // together. Which sources exist is discovered by attribute, so adding one
            // touches only the file that defines it.
            RawModel rawModel = new RawModel();

            using (timings.Measure(RunTimings.Phase.Importing))
                SourceRegistry.ImportAll(options, recipeModel, rawModel, cache.Inputs);


            // Cooking

            var cooker = new ModelCooker();
            Models.Model model;

            using (timings.Measure(RunTimings.Phase.Cooking))
                model = cooker.Cook(options, recipeModel, rawModel);

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
            using (timings.Measure(RunTimings.Phase.Validating))
                validation?.RunPost(model);

            if (options.ValidateOnly)
            {
                LogCategory.Validating.Information("Validation passed. Stopping before any output, as --validate-only asks.");
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

            using (timings.Measure(RunTimings.Phase.Output))
                TargetRegistry.RunAll(options, recipeModel, model, timings, cache);

            LogCategory.Committing.Information("Now that we have completed all the work, we are copying the generated staging files to the destination folder.");

            try
            {
                using (timings.Measure(RunTimings.Phase.Committing))
                {
                    StagingFiles.CommitFiles((filename, stagedFilename) =>
                    {
                        LogCategory.Committing.Debug($"Commit staged file `{filename}`");
                    });
                }
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

            // After the commit, because the output being recorded is at its destination only
            // now - until the move above it was in staging under a name of its own.
            using (timings.Measure(RunTimings.Phase.Sealing))
                cache.Seal();
        }
        catch (Exception ex)
        {
            LogException(options, ex);

            return 1;
        }

        // Only on the way out of a run that finished. The phases of one that stopped
        // part-way say how far it got rather than what the work costs, and they would print
        // under the message saying what went wrong.
        timings.Report();

        return 0;
    }

    private static void LogException(Options options, Exception ex, string subject = "")
    {
        Log.Fatal(ex.Message);

        // Said out loud, because the two kinds of failure need different readers. A data
        // problem names a cell and the person holding the workbook fixes it; this one names
        // nothing they own, and the worst outcome is that they go looking through their
        // sheets for a cause that is not there.
        //
        // The stack comes without `--debugging` being asked for. It is the only part of a
        // defect report that is worth anything to us, and a user who has to be told about a
        // flag first will send the sentence without it.
        if (ex is TabbitDefectException)
        {
            Log.Fatal("This is a defect in tabbit, not a problem with the data or the recipe.");
            Log.Fatal("Please report it with the call stack below.");

            if (ex.StackTrace is not null)
            {
                Log.Fatal("Callstack:");
                Log.Fatal(ex.StackTrace);
            }

            return;
        }

        if (ex is TabbitException tabbitEx)
        {
            if (tabbitEx.Location is not null)
                Log.Fatal($"   at {tabbitEx.Location}");

            if (tabbitEx.Details is not null && tabbitEx.Details.Count > 0)
            {
                // Header printed once, ahead of the list. It used to be inside the
                // loop, so it was repeated before every single entry.
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
            Log.Fatal("Callstack:");
            Log.Fatal(ex.StackTrace);
        }
    }

    /// <summary>
    /// Makes the console take UTF-8, so a message can hold any character this tool might
    /// have to write.
    /// </summary>
    /// <remarks>
    /// A Windows console starts on the system's ANSI codepage, and on a Korean machine that
    /// is 949 - which has no room for kana or for Chinese at all, so those characters leave
    /// as question marks whatever the string held. The encoding is not a display setting
    /// here: the bytes are already gone by the time anything is drawn.
    ///
    /// Called before anything writes, which is why it is the first line of Main rather than
    /// part of <see cref="SetupLogging"/> - the argument parser writes its own errors and
    /// help text, and those come out before a logger exists.
    ///
    /// Without the identifier, so a redirected run does not get a byte-order mark in front
    /// of its first line. Setting this reaches Console.Error too, which is where the run's
    /// own messages go.
    ///
    /// It throws where there is no console to configure - a detached or fully redirected
    /// process on some hosts - and that is not a reason to stop: the streams are already
    /// UTF-8 everywhere except the Windows console, so the platform that does not need this
    /// is also the one that refuses it.
    /// </remarks>
    private static void UseUtf8ForOutput()
    {
        try
        {
            Console.OutputEncoding = Utf8WithoutMark;
        }
        catch (IOException)
        {
        }
        catch (System.Security.SecurityException)
        {
        }
    }

    /// <summary>
    /// UTF-8 that writes no byte-order mark, for the console and the log file alike.
    /// </summary>
    private static readonly System.Text.UTF8Encoding Utf8WithoutMark =
        new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    /// <summary>The environment variable `--messages` can be given through instead.</summary>
    private const string MessageLanguageVariable = "TABBIT_MESSAGES";

    /// <summary>
    /// Settles which language this run's own reports come out in.
    /// </summary>
    /// <remarks>
    /// The flag wins over the variable, which is the usual way round: the variable is how a
    /// machine is set up once and the flag is how one run differs from that.
    ///
    /// A language with no catalog is not refused. Every key falls back to English, so the run
    /// works and says less than it could - which is better than a conversion that will not
    /// start because somebody typed `kr` for `ko`. What is not silent is the shortfall: the
    /// run says at the end how many reports came out in English.
    /// </remarks>
    private static void ChooseMessageLanguage(string asked)
    {
        string language = !string.IsNullOrWhiteSpace(asked)
            ? asked.Trim()
            : Environment.GetEnvironmentVariable(MessageLanguageVariable)?.Trim() ?? "";

        if (language.Length == 0)
            return;

        Messages.MessageCatalog.Current = Messages.MessageCatalog.ForLanguage(language);
    }

    private static void SetupLogging(bool verbose, bool silent)
    {
        Serilog.Events.LogEventLevel loggingLevel = Serilog.Events.LogEventLevel.Information;

        if (silent)
            loggingLevel = Serilog.Events.LogEventLevel.Error;
        else if (verbose)
            loggingLevel = Serilog.Events.LogEventLevel.Debug;

        // The console gets the level as one letter and the file as three, because the two
        // are read differently: the console is watched while the run happens, where a
        // narrow gutter keeps the messages themselves lined up, and the file is read
        // afterwards beside a timestamp that is wide anyway.
        //
        // `,-10` pads the category so the messages line up. Serilog only ever pads, so a
        // longer name pushes its own line out and leaves the rest alone, and `:l` keeps
        // it unquoted.
        const string ConsoleTemplate =
            "[{Level:u1}] [{Category,-10:l}] {Message:lj}{NewLine}{Exception}";

        const string FileTemplate =
            "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] [{Category,-10:l}] "
            + "{Message:lj}{NewLine}{Exception}";

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            // Fills in only where nothing else did: an enricher adds a property when it is
            // absent and leaves it alone otherwise, so a class that declared a category keeps
            // the one it declared and this covers the classes that declared none.
            .Enrich.WithProperty(LogCategory.PropertyName, LogCategory.Default)
            // The theme is named rather than left to the default, because what it colours
            // is now load-bearing: the level marker is what separates a warning from the
            // hundred ordinary lines around it. Serilog drops the colour by itself when the
            // output is redirected, so a piped or captured run stays plain text.
            //
            // The system theme rather than the ANSI one: it goes through the console's own
            // colour API instead of writing escape sequences, so a terminal that does not
            // interpret them shows the line rather than the codes.
            .WriteTo.Console(outputTemplate: ConsoleTemplate,
                            theme: SystemConsoleTheme.Literate,
                            restrictedToMinimumLevel: loggingLevel)
            // The encoding is stated rather than left to the sink's default, for the same
            // reason the console's is: a log file holding a run's reports has to be readable
            // whatever language those reports came out in, and a default that changes
            // between sink versions is not something to find out from a mangled log.
            .WriteTo.File("logs/tabbit.log",
                          outputTemplate: FileTemplate,
                          rollingInterval: RollingInterval.Day,
                          encoding: Utf8WithoutMark)
            .CreateLogger();
    }
}
