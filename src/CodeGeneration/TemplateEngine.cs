using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Scriban;
using Scriban.Parsing;
using Scriban.Runtime;

namespace Tabbit.CodeGeneration;

/// <summary>
/// Renders the embedded code-generation templates.
///
/// The generators used to build their output by calling into a printer line by line,
/// which put the shape of a C++ header - the part someone reviewing the output cares
/// about - inside string literals scattered through several hundred lines of C#. A
/// template puts the shape in one readable place and leaves the C# to work out the
/// values, which is the division that makes a new output language tractable.
///
/// Templates are embedded resources, as the readers are, so what ships cannot drift
/// from what is committed.
/// </summary>
internal static class TemplateEngine
{
    /// <summary>
    /// Renders a template against a model.
    ///
    /// Member names are addressed in the template the way Scriban addresses them by
    /// default - `record_name` for RecordName - which happens to read well here because
    /// the languages being generated are themselves snake_case or camelCase.
    /// </summary>
    /// <param name="templateName">File name under templates/, such as `cpp.sbn`.</param>
    /// <param name="model">The view the template reads.</param>
    public static string Render(string templateName, object model)
        => RenderSource(templateName, Load(templateName), model);

    /// <summary>
    /// Renders template text this repository did not ship.
    /// </summary>
    /// <remarks>
    /// For the one target whose output format is the project's rather than ours: a gathered
    /// text set is written in whatever shape the engine consuming it reads, and there is no
    /// shape this tool could pick that would be right for the next project. The built-in
    /// templates go through <see cref="Render"/> and are still embedded, so what ships cannot
    /// drift from what is committed; this is the door for a recipe that points at its own file.
    ///
    /// <paramref name="templateName"/> is what a parse error is reported against, so pass the
    /// path the recipe wrote rather than a description.
    /// </remarks>
    public static string RenderSource(string templateName, string source, object model)
    {
        var template = Parsed(source, templateName);

        var context = new TemplateContext
        {
            // A typo in a template is a bug in this repository, not something to paper
            // over with an empty string in somebody's generated header.
            StrictVariables = true,

            // A backstop against a template of ours looping forever, and nothing else.
            //
            // It used to be 100,000, which is not a bound on template bugs but a bound on
            // the data: a template that walks rows does one iteration per row, so the
            // limit was really a cap on how many rows a project may have. A real workbook
            // with a 103,398-row table hit it, and what it produced was a template error
            // naming a line number - which reads as a bug in this repository and is not
            // one. Nothing here loops over anything a template author controls, so the
            // number only has to be past what any sheet can reach; a genuinely runaway
            // loop still stops rather than hanging.
            LoopLimit = int.MaxValue,

            // The same mistake as the line above, in the property nobody looked at when that
            // one was raised. Scriban's default caps a render at 1,048,576 characters - and
            // when a render reaches it, Scriban does not fail: it writes an ellipsis and
            // returns, so the file lands on disk looking finished.
            //
            // Two committed sample outputs were sitting like that, both cut mid-token - an
            // Unreal .cpp that stops inside an identifier, and an HTML page with no closing
            // tag. Neither is compiled or opened by any gate, so nothing said a word. Again
            // this is a bound on the data rather than on a template: output grows with rows
            // and columns, so any cap here is a cap on how large a project may be.
            LimitToString = int.MaxValue,

            // So a template can `include` the pieces several pages share - a page head,
            // a footer - instead of each carrying its own copy.
            TemplateLoader = new EmbeddedTemplateLoader(),
        };

        var globals = new ScriptObject();
        globals.Import(model, renamer: member => StandardMemberRenamer.Default(member));
        context.PushGlobal(globals);

        return Normalize(template.Render(context));
    }

    /// <summary>
    /// Resolves `include` against the embedded templates, by file name.
    /// </summary>
    private sealed class EmbeddedTemplateLoader : ITemplateLoader
    {
        public string GetPath(TemplateContext context, SourceSpan callerSpan, string templateName)
            => templateName;

        public string Load(TemplateContext context, SourceSpan callerSpan, string templatePath)
            => TemplateEngine.Load(templatePath);

        public ValueTask<string?> LoadAsync(TemplateContext context, SourceSpan callerSpan, string templatePath)
            => new(Load(context, callerSpan, templatePath));
    }

    /// <summary>
    /// Puts the rendered text into the form a text file is supposed to take.
    ///
    /// Three things:
    ///
    ///   - line endings are LF. The printer this replaced declared CRLF and then
    ///     normalized it away again on the way out, so every generated file has been LF
    ///     all along.
    ///
    ///   - every line is right-trimmed, which matters for templates: an indented line
    ///     whose content turns out to be empty would otherwise leave trailing spaces.
    ///
    ///   - the file ends with exactly one newline. Not two.
    ///
    /// That last one used to be two, and the note here said so: the printer split on the
    /// final newline, which yields one empty segment, then appended a newline to every
    /// segment including that one. An accident, kept while the generators were moved onto
    /// templates so that the golden trees could prove the bytes had not moved.
    ///
    /// That move is long done, and the accident outlived its reason. One trailing newline
    /// is what every tool expects - it is what makes a file's last line a line at all -
    /// and two is a blank line at the end of every generated file that a formatter, a
    /// linter or a reviewer will want to remove.
    /// </summary>
    private static string Normalize(string text)
    {
        var lines = new List<string>(text.Replace("\r\n", "\n").Split('\n'));

        for (int i = 0; i < lines.Count; i++)
            lines[i] = lines[i].TrimEnd();

        // Whatever the template file happened to end with is discarded, so the ending is
        // decided here rather than by an editor's trailing-newline habit.
        while (lines.Count > 0 && lines[^1].Length == 0)
            lines.RemoveAt(lines.Count - 1);

        var result = new StringBuilder(text.Length + 16);

        foreach (var line in lines)
        {
            result.Append(line);
            result.Append('\n');
        }

        return result.ToString();
    }

    /// <summary>
    /// The text of one of the templates this repository ships.
    /// </summary>
    /// <remarks>
    /// Public to the assembly for the target that lets a recipe choose between a shipped
    /// template and one of the project's own: it needs the shipped text without also needing
    /// to know that shipped means embedded.
    /// </remarks>
    /// <summary>
    /// The parsed form of a template, parsed once however many files it renders.
    /// </summary>
    /// <remarks>
    /// **A generator renders its template once per table**, and parsing it again each time is
    /// the larger half of what rendering costs: of 3.88 s spent rendering on the sample
    /// project, 2.41 s was Scriban re-reading templates it had already read. Parsing depends
    /// on the text and nothing else, so it is an answer that can be kept.
    ///
    /// Keyed by the text rather than by the name, because one of the names is a path a recipe
    /// chose - two recipes may point the same name at different files, and a template cached
    /// under a name would then render the wrong one. The text is the identity; the name is
    /// only what a parse error is reported against.
    ///
    /// A parsed template is the syntax tree, and rendering does not write to it - the state a
    /// render accumulates lives in the `TemplateContext` built per call below. So one of
    /// these is safely rendered by several threads, which matters now that the output targets
    /// run beside each other. spec/ops/conversion-time.md section 4.
    /// </remarks>
    private static readonly ConcurrentDictionary<string, Template> ParsedTemplates =
        new ConcurrentDictionary<string, Template>(StringComparer.Ordinal);

    private static Template Parsed(string source, string templateName)
        => ParsedTemplates.GetOrAdd(source, text =>
        {
            var template = Template.Parse(text, templateName);

            if (template.HasErrors)
            {
                throw new TabbitDefectException(
                    $"Template `{templateName}` failed to parse:{Environment.NewLine}" +
                    string.Join(Environment.NewLine, template.Messages));
            }

            return template;
        });

    public static string Load(string templateName)
    {
        string resourceName = "Tabbit.Templates." + templateName;

        using var stream = typeof(TemplateEngine).Assembly.GetManifestResourceStream(resourceName) ?? throw new TabbitDefectException($"Embedded template `{resourceName}` is missing from the build.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
