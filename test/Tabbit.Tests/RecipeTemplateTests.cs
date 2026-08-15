using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Tabbit.Tests;

/// <summary>
/// The starting recipes `--new-recipe --template` writes.
///
/// These are hand-authored, which is the point - a reflected skeleton shows every setting a
/// target takes and answers "what can I write", not "what should I write for a Unity client".
/// Being hand-authored is also how they go stale: rename a setting and the template still
/// looks fine, and the person it fails for is somebody on their first day with the tool.
///
/// So each one is converted for real against a fixture workbook. The converter refuses a
/// setting a target does not have, so running is the check - not reading.
/// </summary>
public class RecipeTemplateTests
{
    private static string TemplateDir => Path.Combine(RepoLayout.Root, "src", "recipes");

    public static TheoryData<string> Templates
    {
        get
        {
            var data = new TheoryData<string>();

            foreach (var path in Directory.GetFiles(TemplateDir, "*.jsonc").OrderBy(p => p))
                data.Add(Path.GetFileNameWithoutExtension(path));

            return data;
        }
    }

    [Fact]
    public void There_is_a_template_for_every_situation_the_readme_offers()
    {
        var names = Directory.GetFiles(TemplateDir, "*.jsonc")
            .Select(Path.GetFileNameWithoutExtension)
            .OrderBy(name => name, StringComparer.Ordinal);

        // Spelled out, so deleting one is a decision rather than a quieter set of options.
        Assert.Equal(
            new[] { "ci", "client-server", "server", "unity", "unreal", "web" },
            names);
    }

    /// <summary>
    /// The CLI writes each one, and the file it writes is the file that is committed.
    /// </summary>
    [Theory]
    [MemberData(nameof(Templates))]
    public void The_cli_writes_the_template(string template)
    {
        string written = Path.Combine(
            RepoLayout.OutputDir("_templates"), template + ".json");

        Directory.CreateDirectory(Path.GetDirectoryName(written));

        var run = TabbitRunner.Invoke("--new-recipe", written, "--template", template);

        Assert.True(run.Succeeded, $"--template {template} failed.{Environment.NewLine}{run.Describe()}");

        Assert.Equal(
            File.ReadAllText(Path.Combine(TemplateDir, template + ".jsonc")).Replace("\r\n", "\n"),
            File.ReadAllText(written).Replace("\r\n", "\n"));
    }

    /// <summary>
    /// And it converts - which is what says the settings in it are real.
    /// </summary>
    /// <remarks>
    /// The template's own paths point at a project that is not here, so the source is swapped
    /// for a fixture workbook and every output path is moved under the test's output. The
    /// settings themselves are left exactly as written, because they are the thing under
    /// test: the converter refuses a setting a target does not have, so a renamed option
    /// fails here.
    ///
    /// Entries that reach a server - a database, the history - are dropped rather than
    /// pointed somewhere. Whether MySQL is up is not what this is asking.
    /// </remarks>
    [Theory]
    [MemberData(nameof(Templates))]
    public void The_template_converts(string template)
    {
        var recipe = JObject.Parse(WithoutComments(
            File.ReadAllText(Path.Combine(TemplateDir, template + ".jsonc"))));

        string outputRoot = Path.Combine(RepoLayout.OutputDir("_templates"), template + "-out");

        // From nothing every time. One of these outputs - the schema baseline - is read back
        // by the next run, so a left-over one makes this test compare two runs instead of
        // checking that the template converts.
        if (Directory.Exists(outputRoot))
            Directory.Delete(outputRoot, recursive: true);

        recipe["Sources"] = JObject.Parse(
            @"{ ""Xlsx"": [ { ""Path"": ""test/fixtures/xlsx/core"" } ] }");

        if (recipe["Targets"] is JArray targets)
        {
            var kept = targets
                .Where(entry => entry["ConnectionString"] == null && (string)entry["Type"] != "history")
                .ToList();

            for (int index = 0; index < kept.Count; index++)
                Repath(kept[index], outputRoot, (string)kept[index]["Type"], index);

            recipe["Targets"] = new JArray(kept);
        }

        // The directory as well as the file. Every other scenario has the converter make its
        // own output tree, so this was the one place that only worked because some earlier
        // test had already created `test/fixtures/output` - delete that tree, which is
        // ignored and derived and therefore deletable, and all six templates failed on a
        // path rather than on anything about a template.
        string prepared = Path.Combine(RepoLayout.OutputDir("_templates"), template + "-prepared.json");
        Directory.CreateDirectory(Path.GetDirectoryName(prepared));
        File.WriteAllText(prepared, recipe.ToString(Formatting.Indented));

        var run = TabbitRunner.Invoke("--recipe", prepared, "--debug");

        Assert.True(run.Succeeded,
            $"The `{template}` template does not convert.{Environment.NewLine}{run.Describe()}");
    }

    /// <summary>
    /// Moves one entry's output under the test's directory, and gives each entry its own.
    /// </summary>
    /// <remarks>
    /// Its own, because a template can hold two entries of one kind - `client-server` exports
    /// binary twice, once per side - and sending both to one directory makes them write
    /// different manifests to the same path. The converter refuses that, correctly; it was
    /// the preparation here that was wrong.
    ///
    /// `SheetBaseline` gets moved for a different reason: it is an output the next run reads
    /// back. Left at the template's relative path it landed in the working directory, which
    /// is the repository, and stayed there - so this test recorded a schema on one run and
    /// compared against it on the next. That made it pass or fail on what a previous run had
    /// left behind, and it did fail, months later, for a schema default that had changed in
    /// between. A test whose verdict depends on its own leftovers is not testing the
    /// template.
    /// </remarks>
    private static void Repath(JToken entry, string outputRoot, string kind, int index)
    {
        string dir = Path.Combine(outputRoot, $"{kind}-{index}");

        if (entry["Path"] != null)
            entry["Path"] = dir.Replace('\\', '/');

        if (entry["SchemaBaseline"] != null)
        {
            entry["SchemaBaseline"] = Path
                .Combine(dir, Path.GetFileName((string)entry["SchemaBaseline"]))
                .Replace('\\', '/');
        }
    }

    /// <summary>
    /// The templates are `.jsonc` - the converter accepts `//` comments and Json.NET's plain
    /// parser does not.
    /// </summary>
    /// <remarks>
    /// Line comments only, which is all the templates use. A `//` inside a string would be
    /// cut, so the check counts quotes ahead of it - crude, and enough for files this test
    /// also proves the converter reads.
    /// </remarks>
    private static string WithoutComments(string text)
    {
        var lines = new List<string>();

        foreach (var line in text.Split('\n'))
        {
            int at = line.IndexOf("//", StringComparison.Ordinal);

            lines.Add(at >= 0 && line.Substring(0, at).Count(c => c == '"') % 2 == 0
                ? line.Substring(0, at)
                : line);
        }

        return string.Join("\n", lines);
    }
}
