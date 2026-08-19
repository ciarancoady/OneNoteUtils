using System.Text.RegularExpressions;
using OneNoteUtils.Core;
using OneNoteUtils.Core.Models;
using OneNoteUtils.Core.Parsing;
using OneNoteUtils.Core.Sync;

namespace OneNoteUtils.Cli;

public sealed record PushOptions(
    bool DryRun = false,
    bool UpdateExisting = false,
    string? TargetPageId = null);

public enum PushAction
{
    Create,
    Update
}

public sealed record PushPlanItem(
    string MarkdownPath,
    string Title,
    IReadOnlyList<ContentElement> Elements,
    PushAction Action,
    string? PageId,
    string? OriginalPageXml);

public sealed record PushPlan(
    string NotebookName,
    string SectionName,
    string SectionId,
    IReadOnlyList<PushPlanItem> Items);

internal static class PushPlanBuilder
{
    private static readonly HashSet<string> AllowedLinkSchemes =
        new(StringComparer.OrdinalIgnoreCase) { "http", "https", "mailto", "onenote" };

    public static PushPlan Build(
        IOneNoteSource source,
        string pushPath,
        string notebookName,
        string sectionName,
        string manifestDirectory,
        PushOptions options)
    {
        var markdownFiles = ResolveMarkdownFiles(pushPath);
        if (markdownFiles == null)
            throw new FileNotFoundException($"Push path '{pushPath}' is not a .md file or directory.");
        if (markdownFiles.Count == 0)
            throw new InvalidOperationException($"No .md files found at '{pushPath}'.");
        if (options.UpdateExisting && options.TargetPageId != null)
            throw new InvalidOperationException(
                "--update-existing and --target-page-id cannot be used together.");
        if (markdownFiles.Count > 1 && options.TargetPageId != null)
            throw new InvalidOperationException("--target-page-id can only be used when pushing one Markdown file.");

        var sectionId = source.FindSectionId(notebookName, sectionName)
            ?? throw new InvalidOperationException(
                $"Section '{sectionName}' was not found in notebook '{notebookName}'.");
        var section = ResolveSection(source.GetHierarchyXml(), notebookName, sectionName)
            ?? throw new InvalidOperationException(
                $"Section '{sectionName}' was not found in notebook '{notebookName}'.");

        var manifest = SyncManifest.Load(manifestDirectory);
        var items = new List<PushPlanItem>();
        var claimedPageIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var createTitles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var markdownFile in markdownFiles)
        {
            var markdown = File.ReadAllText(markdownFile);
            ValidateRawLinks(markdown);
            var elements = MarkdownReader.Parse(markdown, Path.GetDirectoryName(markdownFile));
            ValidateElements(elements);
            var title = MarkdownReader.ExtractTitle(markdown) ?? Path.GetFileNameWithoutExtension(markdownFile);
            var normalizedTitle = NormalizeTitle(title);

            string? pageId = null;
            if (options.TargetPageId != null)
            {
                pageId = section.Pages.FirstOrDefault(page =>
                    page.PageId.Equals(options.TargetPageId, StringComparison.OrdinalIgnoreCase))?.PageId
                    ?? throw new InvalidOperationException(
                        $"Page ID '{options.TargetPageId}' does not belong to '{notebookName}' / '{sectionName}'.");
            }
            else if (manifest.Pushed.TryGetValue(markdownFile, out var tracked))
            {
                pageId = section.Pages.FirstOrDefault(page =>
                    page.PageId.Equals(tracked.PageId, StringComparison.OrdinalIgnoreCase))?.PageId
                    ?? throw new InvalidOperationException(
                        $"Tracked Page '{tracked.PageId}' no longer belongs to '{notebookName}' / '{sectionName}'.");
            }
            else
            {
                var matches = section.Pages
                    .Where(page => NormalizeTitle(page.Title) == normalizedTitle)
                    .ToList();
                if (matches.Count > 1)
                    throw new InvalidOperationException(
                        $"Multiple Pages named '{title}' exist. Use --target-page-id.");
                if (matches.Count == 1)
                {
                    if (!options.UpdateExisting)
                        throw new InvalidOperationException(
                            $"A Page named '{title}' already exists. Use --update-existing or --target-page-id.");
                    pageId = matches[0].PageId;
                }
            }

            if (pageId != null && !claimedPageIds.Add(pageId))
                throw new InvalidOperationException($"More than one Markdown file targets Page '{pageId}'.");
            if (pageId == null && !createTitles.Add(normalizedTitle))
                throw new InvalidOperationException($"More than one Markdown file would create a Page named '{title}'.");

            var originalXml = pageId != null && !options.DryRun
                ? source.GetPageContentXml(pageId)
                : null;
            items.Add(new PushPlanItem(
                markdownFile,
                title,
                elements,
                pageId == null ? PushAction.Create : PushAction.Update,
                pageId,
                originalXml));
        }

        return new PushPlan(notebookName, sectionName, sectionId, items);
    }

    private static Section? ResolveSection(string hierarchyXml, string notebookName, string sectionName)
    {
        var notebook = HierarchyParser.ParseNotebook(
            hierarchyXml,
            notebookName,
            new ExportOptions { SectionFilter = [sectionName] });
        return notebook?.GetAllSections()
            .Select(item => item.Section)
            .FirstOrDefault(section => section.Name.Equals(sectionName, StringComparison.OrdinalIgnoreCase));
    }

    private static void ValidateRawLinks(string markdown)
    {
        foreach (Match match in Regex.Matches(markdown, @"!?\[[^\]]*\]\(([^)\s]+)"))
            ValidateTarget(match.Groups[1].Value, isImage: match.Value.StartsWith('!'));
    }

    private static void ValidateElements(IEnumerable<ContentElement> elements)
    {
        foreach (var element in elements)
        {
            switch (element)
            {
                case Image image:
                    if (image.Source != null)
                        ValidateTarget(image.Source, isImage: true);
                    if (image.LoadBytes() == null)
                        throw new FileNotFoundException($"Image '{image.FileName}' could not be resolved.");
                    break;
                case Paragraph paragraph:
                    foreach (var run in paragraph.Runs.Where(run => run.HrefUrl != null))
                        ValidateTarget(run.HrefUrl!, isImage: false);
                    break;
                case Checkbox checkbox:
                    foreach (var run in checkbox.Runs.Where(run => run.HrefUrl != null))
                        ValidateTarget(run.HrefUrl!, isImage: false);
                    break;
                case BulletList bulletList:
                    foreach (var item in bulletList.Items)
                    {
                        ValidateElements(item.Elements);
                        if (item.Children != null) ValidateElements(item.Children);
                    }
                    break;
                case NumberedList numberedList:
                    foreach (var item in numberedList.Items)
                    {
                        ValidateElements(item.Elements);
                        if (item.Children != null) ValidateElements(item.Children);
                    }
                    break;
                case Table table:
                    ValidateElements(table.Rows.SelectMany(row => row.Cells).SelectMany(cell => cell.Elements));
                    break;
                case Blockquote blockquote:
                    ValidateElements(blockquote.Elements);
                    break;
            }
        }
    }

    private static void ValidateTarget(string target, bool isImage)
    {
        target = target.Trim().Trim('<', '>');
        if (isImage && Path.IsPathRooted(target))
            return;
        if (!Uri.TryCreate(target, UriKind.Absolute, out var uri))
            return;

        if (isImage && uri.Scheme is "http" or "https")
            throw new InvalidOperationException(
                $"Remote image '{target}' is not allowed. Download it beside the Markdown file first.");
        if (!AllowedLinkSchemes.Contains(uri.Scheme))
            throw new InvalidOperationException(
                $"Link scheme '{uri.Scheme}' is not allowed in Push.");
    }

    private static string NormalizeTitle(string title) => title.Trim().ToUpperInvariant();

    private static List<string>? ResolveMarkdownFiles(string pushPath)
    {
        if (File.Exists(pushPath) && pushPath.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            return [Path.GetFullPath(pushPath)];
        if (!Directory.Exists(pushPath))
            return null;
        return Directory.GetFiles(pushPath, "*.md", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFullPath)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
