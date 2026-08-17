using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OneNoteUtils.Cli;
using OneNoteUtils.Core;
using OneNoteUtils.OneNote;
using OneNoteUtils.Writers.Obsidian;

var parsed = CliArguments.Parse(args);
if (parsed.Help)
{
    CliArguments.PrintUsage();
    return 0;
}

if (!string.IsNullOrEmpty(parsed.PushPath))
{
    if (string.IsNullOrEmpty(parsed.Notebook) || parsed.Sections.Count == 0)
    {
        Console.Error.WriteLine("Push requires --notebook and --section.");
        return 1;
    }
}
else if (string.IsNullOrEmpty(parsed.Notebook) || string.IsNullOrEmpty(parsed.Output))
{
    CliArguments.PrintUsage();
    return 1;
}

var configBuilder = new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json", optional: true);
if (!string.IsNullOrEmpty(parsed.Config))
    configBuilder.AddJsonFile(parsed.Config, optional: false);

var options = new ExportOptions
{
    NotebookIdentifier = parsed.Notebook ?? "",
    OutputPath = parsed.Output ?? ""
};
configBuilder.Build().GetSection("ExportOptions").Bind(options);
options.NotebookIdentifier = parsed.Notebook ?? "";
options.OutputPath = parsed.Output ?? "";
if (parsed.Sections.Count > 0)
    options.SectionFilter = parsed.Sections;
if (parsed.Pages.Count > 0)
    options.PageFilter = parsed.Pages;

var services = new ServiceCollection();
services.AddLogging(builder =>
{
    builder.AddConsole();
    builder.SetMinimumLevel(parsed.Verbose ? LogLevel.Debug : LogLevel.Information);
});
services.AddSingleton(options);
services.AddTransient<IOneNoteSource, ComOneNoteSource>();
services.AddSingleton<INotebookWriter, ObsidianMarkdownWriter>();
services.AddTransient<OneNoteApplication>();

using var provider = services.BuildServiceProvider();
var application = provider.GetRequiredService<OneNoteApplication>();

if (!string.IsNullOrEmpty(parsed.PushPath))
    return application.RunPush(parsed.PushPath, parsed.Notebook!, parsed.Sections[0], parsed.Output ?? ".");
if (parsed.FullExport)
    return application.RunFullExport(parsed.Notebook!, parsed.Output!, options);
return application.RunSync(parsed.Notebook!, parsed.Output!, options, parsed.DryRun);

public sealed record CliArguments(
    string? Notebook,
    string? Output,
    string? Config,
    bool Verbose,
    List<string> Sections,
    List<string> Pages,
    bool FullExport,
    string? PushPath,
    bool DryRun,
    bool Help)
{
    public static CliArguments Parse(string[] args)
    {
        string? notebook = null;
        string? output = null;
        string? config = null;
        string? pushPath = null;
        var verbose = false;
        var fullExport = false;
        var dryRun = false;
        var help = false;
        var sections = new List<string>();
        var pages = new List<string>();

        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--notebook" or "-n":
                    if (index + 1 < args.Length) notebook = args[++index];
                    break;
                case "--output" or "-o":
                    if (index + 1 < args.Length) output = args[++index];
                    break;
                case "--config" or "-c":
                    if (index + 1 < args.Length) config = args[++index];
                    break;
                case "--section" or "-s":
                    if (index + 1 < args.Length) sections.Add(args[++index]);
                    break;
                case "--page" or "-p":
                    if (index + 1 < args.Length) pages.Add(args[++index]);
                    break;
                case "--push":
                    if (index + 1 < args.Length) pushPath = args[++index];
                    break;
                case "--full":
                    fullExport = true;
                    break;
                case "--dry-run":
                    dryRun = true;
                    break;
                case "--verbose" or "-v":
                    verbose = true;
                    break;
                case "--help" or "-h":
                    help = true;
                    break;
            }
        }

        return new CliArguments(
            notebook,
            output,
            config,
            verbose,
            sections,
            pages,
            fullExport,
            pushPath,
            dryRun,
            help);
    }

    public static void PrintUsage()
    {
        Console.WriteLine("""
            OneNoteUtils - Sync OneNote notebooks with Obsidian Markdown

            Usage (sync/export):
              OneNoteUtils.Cli -n <notebook> -o <path> [options]

            Usage (push to OneNote):
              OneNoteUtils.Cli --push <file-or-folder> -n <notebook> -s <section>

            Required (sync/export):
              -n, --notebook <name>    Notebook name or folder path
              -o, --output <path>      Output directory

            Required (push):
              --push <path>            .md file or folder to push to OneNote
              -n, --notebook <name>    Target notebook name
              -s, --section <name>     Target section name

            Options:
              -s, --section <name>     Filter sections for sync (repeatable)
              -p, --page <name-or-id>  Filter pages for sync (repeatable)
                  --full               Force full export (skip incremental sync)
                  --dry-run            Preview sync plan without writing files
              -c, --config <path>      Path to a JSON config file (default: appsettings.json)
              -v, --verbose            Enable debug logging
              -h, --help               Show this help message

            Examples:
              OneNoteUtils.Cli -n "My Notebook" -o C:\Export
              OneNoteUtils.Cli -n "My Notebook" -o C:\Export -s "Daily Notes"
              OneNoteUtils.Cli -n "My Notebook" -o C:\Export -s "Daily Notes" -p "Handoff"
              OneNoteUtils.Cli -n "My Notebook" -o C:\Export --full
              OneNoteUtils.Cli -n "My Notebook" -o C:\Export --dry-run
              OneNoteUtils.Cli --push "C:\Vault\Note.md" -n "Team Notebook" -s "Shared"
            """);
    }
}

public partial class Program;
