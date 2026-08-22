using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Tabbit.Helpers;
using Tabbit.Recipe;
using Tabbit.Messages;

namespace Tabbit.Validation;

/// <summary>
/// The opening lines of a new rule file.
/// </summary>
/// <remarks>
/// One line, and the run does not need it: the same `using static` is opened for every rule
/// whether or not the file says so. It is written anyway because a file that names what it uses
/// reads better than one where the names arrive from nowhere.
///
/// A project for an editor to resolve `Tables` through is written too, but only when the recipe
/// asks - `Validation.EmitIdeProject`. It was on by default once and that was wrong: Visual
/// Studio finds the loose project, tries to load it, and fails to resolve `Microsoft.NET.Sdk`
/// when it predates the framework the project targets. An error dialog in exchange for an
/// autocompletion nobody had confirmed. spec/validation-pipeline.md §11.
/// </remarks>
internal static class RuleScaffold
{
    /// <summary>Name of the project written beside the accessor, when the recipe asks for one.</summary>
    private const string ProjectName = "Validation.csproj";

    /// <summary>Where the contract is written, relative to the validation folder.</summary>
    private const string ContractFolder = RuleFolders.ContractFolder;

    /// <summary>Where the editor's project keeps what building it produces.</summary>
    private const string BuildFolder = RuleFolders.BuildFolder;

    /// <summary>The contract's assembly name, which is also what its files are called.</summary>
    private const string ContractAssembly = "Tabbit.Validation";

    /// <summary>
    /// The one package the contract's own surface exposes, carried beside it.
    /// </summary>
    /// <remarks>
    /// `Json` answers with a `JToken`, so a rule that calls it needs the assembly that type is in -
    /// and without it the editor's project reports CS0012 on a method the contract advertises.
    ///
    /// Written beside the contract rather than named as a `PackageReference`, for the reason the
    /// contract itself is: everything this project names is in this folder, so a clone has
    /// completion with nothing fetched and nothing restored.
    /// </remarks>
    private static readonly string[] CarriedPackages = ["Newtonsoft.Json"];

    /// <summary>
    /// Puts the contract beside the rules, so the editor's project can name it by a relative path.
    /// </summary>
    /// <remarks>
    /// **This is what makes the project committable.** It used to point at
    /// `typeof(IContext).Assembly.Location` - wherever the tool happened to be on the machine that
    /// ran the conversion - so the project could not be shared, and a clone had no completion until
    /// somebody ran a conversion. A file beside the rules is the same on every machine.
    ///
    /// Written from what this assembly carries rather than copied off disk, because a single-file
    /// build has no disk copy to take.
    ///
    /// **Only when the bytes differ.** These files are committed, and a build of the tool that
    /// changed nothing about them must not show up as a change in somebody else's repository. The
    /// contract is built deterministically for the same reason.
    /// </remarks>
    private static List<string> WriteContract(RuleFolders folders)
    {
        string folder = Path.Combine(folders.Root, ContractFolder);
        var written = new List<string>();

        // The summaries travel with it: an editor that resolves the name but cannot say what it
        // does has answered half the question.
        if (WriteIfDifferent(folder, ContractAssembly + ".dll"))
            written.Add(ContractAssembly);

        WriteIfDifferent(folder, ContractAssembly + ".xml");

        foreach (string package in CarriedPackages)
        {
            if (WriteIfDifferent(folder, package + ".dll"))
                written.Add(package);
        }

        return written;
    }

    private static bool WriteIfDifferent(string folder, string name)
    {
        byte[]? carried = RuleCompiler.ReadCarriedFile(name);

        if (carried is null)
            return false;

        WriteBytesIfDifferent(Path.Combine(folder, name), carried);

        return true;
    }

    /// <summary>Writes a file only when its bytes are not already what they should be.</summary>
    /// <remarks>
    /// These files are committed. A run that produced the same result must leave the repository
    /// alone, or every conversion is a diff nobody asked for.
    /// </remarks>
    private static void WriteBytesIfDifferent(string path, byte[] bytes)
    {
        if (bytes is null || bytes.Length == 0)
            return;

        if (File.Exists(path) && File.ReadAllBytes(path).AsSpan().SequenceEqual(bytes))
            return;

        FileHelper.EnsurePathExists(path);
        File.WriteAllBytes(path, bytes);
    }

    /// <summary>
    /// The header a rule file opens with.
    /// </summary>
    /// <remarks>
    /// Two ordinary `using` lines and nothing else - no `#:project`, no `using static`. Both names
    /// a rule uses are visible in the file: the accessor's namespace for `Tables`, and this
    /// assembly's for the `Context` that `Validate` receives. That is what lets an editor resolve
    /// them without having compiled something the author never opened.
    /// </remarks>
    internal static string Header(RuleStage stage)
        => stage == RuleStage.Pre

            // A `pre` rule runs before the sheets are read, so the accessor does not exist yet and
            // a using for its namespace would be an error. Its context is the whole of what it gets.
            ? $"using {typeof(IPreContext).Namespace};\n"
            : $"using {RuleAccessor.Namespace};\nusing {typeof(IPreContext).Namespace};\n";

    /// <summary>
    /// Writes a starting rule file for one table, and answers where it went.
    /// </summary>
    /// <remarks>
    /// Refuses to overwrite. A rule file is somebody's work, and a scaffolding command that
    /// silently replaced one would be a command nobody could run twice.
    /// </remarks>
    internal static string WriteNewValidator(ValidationRecipe recipe, string tableName)
    {
        if (recipe is null || string.IsNullOrWhiteSpace(recipe.Path))
        {
            throw new TabbitException(null, Message.Of(ValidationMessages.NoPathForNewRule));
        }

        if (string.IsNullOrWhiteSpace(tableName))
            throw new TabbitException(null, Message.Of(ValidationMessages.NewValidatorNeedsTable));

        string path = Path.GetFullPath(Path.Combine(
            recipe.Path,
            RuleFolders.FolderOf(RuleStage.Table),
            tableName + RuleFolders.RuleSuffix + ".cs"));

        if (File.Exists(path))
        {
            throw new TabbitException(null,
                Message.Of(ValidationMessages.RuleFileExists, ("Path", path)));
        }

        FileHelper.EnsurePathExists(path);

        string body = $$"""
            {{Header(RuleStage.Table)}}
            // Rules for the `{{tableName}}` table. The file name up to `{{RuleFolders.RuleSuffix}}` is what binds
            // them to it, so a renamed table wants this file renamed too - the run refuses a name
            // no table has, rather than letting the checks stop happening quietly.
            //
            // `context.Tables` is the accessor generated from the sheets, so every field is typed
            // and a misspelling is a compile error - the static `Tables` reaches the same data
            // globally if you would rather. `context` is also how a rule reports: pass the row and
            // the field and the message points at the cell. `context.Table` is this table's
            // schema, for a check about the columns rather than the rows.

            internal static class {{tableName}}{{RuleFolders.RuleSuffix}}
            {
                public static void Validate(ITableContext context)
                {
                    foreach (var row in context.Tables.{{tableName}}.Records)
                    {
                        // if (row.Something < 0)
                        //     context.Error(row, nameof(row.Something), "Cannot be negative.");
                    }
                }
            }

            """;

        File.WriteAllText(path, body.Replace("\r\n", "\n"));

        return path;
    }

    /// <summary>
    /// Writes out the rules in the order they would run.
    /// </summary>
    /// <remarks>
    /// The answer to the one thing an attribute on each rule cannot give: the whole order in one
    /// place. A file listing the order could give it too, but would then be a second thing to keep
    /// in step - and what it said would be a claim rather than the order itself. This is read off
    /// the same folders and the same attributes the run reads.
    /// </remarks>
    internal static string DescribeOrder(RuleFolders folders)
    {
        var text = new System.Text.StringBuilder();

        text.AppendLine($"Validation rules in `{folders.Root}`, in the order they run.");

        foreach (var stage in new[] { RuleStage.Pre, RuleStage.Table, RuleStage.Global, RuleStage.Runtime })
        {
            var files = folders.Of(stage);

            if (files.Count == 0)
                continue;

            text.AppendLine();

            if (stage == RuleStage.Table)
            {
                // No tiers here, and saying so is the point: a reader looking for the order of
                // table rules should find out there is not one rather than find nothing.
                text.AppendLine($"{stage} - {files.Count} rule(s), all at the same time:");

                foreach (var file in files)
                    text.AppendLine($"    {file.Display}");

                continue;
            }

            text.AppendLine($"{stage} - {files.Count} rule(s):");

            foreach (var tier in files.GroupBy(file => file.EffectiveTier).OrderBy(tier => tier.Key))
            {
                string mark = tier.Key == RuleFolders.DefaultTier ? " (default)" : "";

                text.AppendLine($"  tier {tier.Key}{mark}");

                foreach (var file in tier)
                    text.AppendLine($"    {file.Display}");
            }
        }

        return text.ToString();
    }

    /// <summary>The generated accessor's assembly name, and what its files are called.</summary>
    private const string AccessorAssembly = "Tabbit.Rules.Data";

    /// <summary>
    /// A path as this project has to name it: relative to the folder the project sits in.
    /// </summary>
    /// <remarks>
    /// Everything this file names has to be relative, or it is a file that only works on the
    /// machine that wrote it - which is the thing that kept it out of version control.
    /// </remarks>
    private static string RelativeToProject(RuleFolders folders, string path)
        => Path.GetRelativePath(folders.Root, Path.GetFullPath(path)).Replace('\\', '/');

    /// <summary>
    /// Writes the project an editor resolves the accessor through, unless the recipe declines it.
    /// </summary>
    /// <remarks>
    /// Holds the generated accessor **and the rule files**, which is the whole point: a file
    /// belonging to no project has no references to resolve, so an editor completes nothing in it.
    /// Naming a project from the file with `#:project` was tried and does not work either - that is
    /// an SDK directive rather than C#, and the editor's Roslyn reads it as a syntax error on line
    /// one and gives up on the file.
    ///
    /// It lands at the validation folder's root rather than inside `.generated`, and that is not
    /// tidiness: an editor has to find it. A folder whose name begins with a dot is skipped by most
    /// project discovery, so a project hidden one level down in a dot folder is a project nothing
    /// opens.
    ///
    /// **A project and no solution**, deliberately. The C# Dev Kit opens a folder holding a project
    /// without one, and writing a second solution into a repository that already has one turns
    /// that into a choice somebody has to make in the status bar every time. So the validation
    /// folder is opened as its own folder - or added to the workspace - and there is nothing to
    /// pick.
    ///
    /// A Visual Studio older than the framework cannot load it at all - `Microsoft.NET.Sdk` does
    /// not resolve - which is what `EmitIdeProject: false` is for. Such an editor cannot open this
    /// repository's own project either.
    /// </remarks>
    internal static void WriteIdeProject(
        RuleFolders folders, Options options, byte[] accessor, byte[] documentation)
    {
        // Where the conversion was run from. A recipe's own paths are relative to that, so the
        // build has to start in the same place - named relative to this project, since that is
        // the only thing this file may name.
        string here = Directory.GetCurrentDirectory();

        string workingDirectory = RelativeToProject(folders, here);

        // And the recipe as seen from there rather than from the project, because that is where
        // the command runs.
        string recipePath = Path
            .GetRelativePath(here, Path.GetFullPath(options.RecipeFilename ?? ""))
            .Replace('\\', '/');

        var carried = WriteContract(folders);

        // The accessor as an assembly rather than the sources it was compiled from. Nothing edits
        // those sources and nothing but the compiler reads them, so what a project keeps is one
        // file - and a validation folder stops holding a hundred generated ones.
        string lib = Path.Combine(folders.Root, ContractFolder);

        WriteBytesIfDifferent(Path.Combine(lib, AccessorAssembly + ".dll"), accessor);
        WriteBytesIfDifferent(Path.Combine(lib, AccessorAssembly + ".xml"), documentation);

        // What earlier versions left. The folder was ours and is not written any more, so leaving
        // it would mean a stale accessor sitting in the project an editor reads - the same names
        // from before the schema changed, resolving.
        string stale = folders.GeneratedPath;

        if (Directory.Exists(stale))
            Directory.Delete(stale, recursive: true);

        string reference = carried.Count == 0
            ? ""
            : $"""

                <ItemGroup>
                  <!--
                    Where Error, Warn, Info, Option, Files and Db come from - and, beside it, the one
                    package the contract's own surface exposes. `Json` answers with a `JToken`, so a
                    rule calling it needs that assembly named here or the project reports CS0012 on
                    a method the contract advertises.
                  -->
              {string.Join("\n", carried.Select(name => $"""
                    <Reference Include="{name}">
                      <HintPath>{ContractFolder}/{name}.dll</HintPath>
                      <Private>false</Private>
                    </Reference>
              """))}
                </ItemGroup>
              """;

        string project = $"""
            <!--
              Written by Tabbit for an editor, on every run. Nothing builds it and nothing at
              run time reads it: the validation host compiles the accessor and the rules itself.
              `Validation.EmitIdeProject: false` in the recipe stops it being written.

              **Commit it, along with lib/.** Everything it names is either in this folder or
              beside it, so a clone gets completion without running anything first. Do not edit
              it - the next run overwrites it, and it is rewritten only when its contents change.
            -->
            <Project>

              <!--
                Where the build puts its scratch. One hidden folder rather than `bin/` and `obj/`
                beside the rules: this folder is meant to be opened and read, and two build folders
                at the top of the listing are two thirds of what somebody sees first.

                It has to be set before the SDK is imported, which is why this project imports it by
                hand rather than with `<Project Sdk="...">`. Set afterwards, the property moves the
                compiler's output and leaves restore writing `obj/` anyway.
              -->
              <PropertyGroup>
                <BaseIntermediateOutputPath>{BuildFolder}/obj/</BaseIntermediateOutputPath>
                <BaseOutputPath>{BuildFolder}/bin/</BaseOutputPath>
              </PropertyGroup>

              <Import Project="Sdk.props" Sdk="Microsoft.NET.Sdk" />

              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <Nullable>disable</Nullable>
                <LangVersion>latest</LangVersion>

                <!--
                  The same names the host opens for every rule. `Tables` and `Context` are not among
                  them: a rule file names those with ordinary `using` lines, so what an editor sees
                  is what the run compiles and nothing arrives from a file nobody opened.
                -->
                <ImplicitUsings>enable</ImplicitUsings>
                <AssemblyName>Tabbit.Rules.Data</AssemblyName>
                <RootNamespace>{RuleAccessor.Namespace}</RootNamespace>
                <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
                <NoWarn>CS1591</NoWarn>
              </PropertyGroup>

              <ItemGroup>
                <!--
                  The accessor as an assembly. It used to be the hundred sources it is compiled
                  from, which an editor read and nobody edited - so a validation folder carried
                  a generated file per table for the sake of completion on one name.
                -->
                <Reference Include="{AccessorAssembly}">
                  <HintPath>{ContractFolder}/{AccessorAssembly}.dll</HintPath>
                  <Private>false</Private>
                </Reference>
              </ItemGroup>

              <ItemGroup>

                <!--
                  The rules themselves, and the code they share. Listing them here is what makes an
                  editor able to complete `Tables.` and `context.` at all.

                  Which is also why a rule file is a class with a `Validate` method rather than a
                  list of statements. Top-level statements are allowed in one compilation unit, so
                  several such files in one project bind none of them - measured: three files each
                  holding an undefined symbol report CS8802 and not one CS0103.

                  One glob rather than one per stage: which stage a file belongs to is decided by
                  the folder it sits in, and this project does not need to know.
                -->
                <Compile Include="{RuleFolders.RulesFolder}/**/*.cs" />
              </ItemGroup>
            {reference}
              <!--
                Building this project validates. The compiler checks the rules - a column that is
                not there, an enum label that is not there - and then the tool runs them against
                the data, so one keystroke in an editor answers both halves.

                An `Exec` rather than an entry point, deliberately: a `Program.cs` in a folder of
                rules is a file that belongs to neither, and nothing in it would do more than this
                line. Build the project (Ctrl+Shift+B in most editors) rather than run it.

                `tabbit` is taken from PATH, which is where the install instructions put it. The
                alternative would be this file naming wherever the tool sat on the machine that
                wrote it, and that is exactly what stopped this project being committable.

                Set `TabbitValidate=false` to build without running - useful while a rule is
                half-written and the data would only report the obvious.
              -->
              <Target Name="Validate" AfterTargets="Build" Condition="'$(TabbitValidate)' != 'false'">
                <Exec Command="tabbit --recipe &quot;{recipePath}&quot; --validate-only"
                      WorkingDirectory="{workingDirectory}"
                      ContinueOnError="true">
                  <Output TaskParameter="ExitCode" PropertyName="TabbitExitCode" />
                </Exec>

                <!--
                  A missing tool and a failed validation are different answers and must not look
                  alike. 9009 on Windows and 127 elsewhere are the shell saying it found nothing
                  to run - that is a warning about this machine. Anything else non-zero is the
                  data being wrong, which is what this project is for, so it stops the build.
                -->
                <Warning Condition="'$(TabbitExitCode)' == '9009' or '$(TabbitExitCode)' == '127'"
                         Text="The rules compiled, but `tabbit` is not on PATH so they were not run against the data. Add it - see the install instructions - or set TabbitValidate=false to stop asking." />

                <Error Condition="'$(TabbitExitCode)' != '0' and '$(TabbitExitCode)' != '9009' and '$(TabbitExitCode)' != '127'"
                       Text="Validation did not pass. The reports are above, each naming the cell it is about." />
              </Target>

              <Import Project="Sdk.targets" Sdk="Microsoft.NET.Sdk" />

            </Project>

            """.Replace("\r\n", "\n");

        Write(Path.Combine(folders.Root, ProjectName), project);
    }

    private static void Write(string path, string contents)
    {
        FileHelper.EnsurePathExists(path);
        File.WriteAllText(path, contents);
    }
}
