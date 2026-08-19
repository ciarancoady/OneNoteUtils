using Microsoft.Extensions.Logging;
using OneNoteUtils.Core;
using OneNoteUtils.Core.Models;
using OneNoteUtils.Core.Parsing;
using OneNoteUtils.Core.Sync;

namespace OneNoteUtils.Cli;

public sealed class OneNoteApplication(
    IOneNoteSource source,
    INotebookWriter writer,
    ILogger<OneNoteApplication> logger)
{
    public int RunSync(string notebookName, string outputPath, ExportOptions options, bool dryRun = false)
    {
        try
        {
            var manifest = SyncManifest.Load(outputPath);
            logger.LogInformation(
                manifest.Pages.Count == 0
                    ? "No sync manifest found - performing initial full export."
                    : "Sync manifest loaded: {PageCount} pages from last sync.",
                manifest.Pages.Count);

            logger.LogInformation("Reading notebook hierarchy...");
            var notebook = HierarchyParser.ParseNotebook(source.GetHierarchyXml(), notebookName, options);
            if (notebook == null)
            {
                logger.LogError("Notebook '{Notebook}' not found. Make sure it is open in OneNote.", notebookName);
                return 1;
            }

            var plan = SyncDiffer.Diff(manifest, notebook, options);
            logger.LogInformation(
                "Sync plan: {New} new, {Modified} modified, {Deleted} deleted, {Unchanged} unchanged",
                plan.NewPages.Count,
                plan.ModifiedPages.Count,
                plan.DeletedPages.Count,
                plan.UnchangedPages.Count);

            if (dryRun)
            {
                foreach (var page in plan.NewPages)
                    logger.LogInformation("  [NEW] {Title} ({Section})", page.Title, page.Section);
                foreach (var page in plan.ModifiedPages)
                    logger.LogInformation("  [MOD] {Title} ({Section})", page.Title, page.Section);
                foreach (var page in plan.DeletedPages)
                    logger.LogInformation("  [DEL] {Title} ({Section})", page.Title, page.Section);
                logger.LogInformation("Dry run complete. No files were changed.");
                return 0;
            }

            if (plan.TotalWork == 0)
            {
                logger.LogInformation("Nothing to sync - everything is up to date.");
                return 0;
            }

            foreach (var deleted in plan.DeletedPages)
            {
                if (deleted.PreviousEntry == null)
                    continue;

                DeletePageFiles(deleted.PreviousEntry);
                manifest.Pages.Remove(deleted.PageId);
            }

            foreach (var modified in plan.ModifiedPages)
            {
                if (modified.PreviousEntry != null)
                    DeletePageFiles(modified.PreviousEntry);
            }

            var pagesToExport = plan.NewPages.Concat(plan.ModifiedPages).ToList();
            var populatedNotebook = PopulateNotebook(notebook, pagesToExport, out var failedPages);

            foreach (var action in pagesToExport)
            {
                var section = populatedNotebook.Sections.FirstOrDefault(candidate => candidate.Name == action.Section);
                var page = section?.Pages.FirstOrDefault(candidate => candidate.PageId == action.PageId);
                if (page == null)
                    continue;

                try
                {
                    var result = writer.WritePage(page, action.Section, populatedNotebook, outputPath);
                    manifest.Pages[action.PageId] = new SyncPageEntry
                    {
                        Title = action.Title,
                        Section = action.Section,
                        LastModified = action.LastModified,
                        ExportedPath = result.ExportedPath,
                        ExportedFiles = result.ExportedFiles
                    };
                }
                catch (Exception ex)
                {
                    logger.LogWarning("Failed to write page '{PageTitle}': {Error}", action.Title, ex.Message);
                }
            }

            var inkPdfs = ExportInkPdfs(populatedNotebook, outputPath);
            foreach (var (pageId, pdfPath) in inkPdfs)
            {
                if (manifest.Pages.TryGetValue(pageId, out var entry) && !entry.ExportedFiles.Contains(pdfPath))
                    entry.ExportedFiles.Add(pdfPath);
            }

            manifest.NotebookName = notebook.Name;
            manifest.LastSyncTime = DateTime.UtcNow;
            manifest.Save(outputPath);

            logger.LogInformation(
                "Sync complete. {Exported} pages synced ({Failed} failed), {Deleted} deleted.",
                pagesToExport.Count - failedPages,
                failedPages,
                plan.DeletedPages.Count);
            return 0;
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex, "Sync failed: {Error}", ex.Message);
            return 1;
        }
    }

    public int RunFullExport(string notebookName, string outputPath, ExportOptions options)
    {
        try
        {
            logger.LogInformation(
                "Starting full export of notebook '{Notebook}' to '{Output}'",
                notebookName,
                outputPath);

            var notebook = HierarchyParser.ParseNotebook(source.GetHierarchyXml(), notebookName, options);
            if (notebook == null)
            {
                logger.LogError("Notebook '{Notebook}' not found. Make sure it is open in OneNote.", notebookName);
                return 1;
            }

            var notebookFolder = Path.Combine(outputPath, FileNameUtils.SanitizeFileBaseName(notebook.Name));
            if (Directory.Exists(notebookFolder))
                Directory.Delete(notebookFolder, recursive: true);

            var allPages = notebook.GetAllSections()
                .SelectMany(item => item.Section.Pages.Select(page => new SyncPageAction
                {
                    PageId = page.PageId,
                    Title = page.Title,
                    Section = string.IsNullOrEmpty(item.Path) ? item.Section.Name : $"{item.Path}/{item.Section.Name}",
                    LastModified = page.LastModified
                }))
                .ToList();
            var populatedNotebook = PopulateNotebook(notebook, allPages, out var failedPages);
            var writeResults = writer.Write(populatedNotebook, outputPath);
            var inkPdfs = ExportInkPdfs(populatedNotebook, outputPath);

            var manifest = new SyncManifest
            {
                NotebookName = notebook.Name,
                LastSyncTime = DateTime.UtcNow
            };

            foreach (var section in populatedNotebook.Sections)
            {
                foreach (var page in section.Pages)
                {
                    var entry = new SyncPageEntry
                    {
                        Title = page.Title,
                        Section = section.Name,
                        LastModified = page.LastModified
                    };

                    if (writeResults.TryGetValue(page.PageId, out var result))
                    {
                        entry.ExportedPath = result.ExportedPath;
                        entry.ExportedFiles = result.ExportedFiles;
                    }

                    if (inkPdfs.TryGetValue(page.PageId, out var pdfPath) &&
                        !entry.ExportedFiles.Contains(pdfPath))
                        entry.ExportedFiles.Add(pdfPath);

                    manifest.Pages[page.PageId] = entry;
                }
            }

            manifest.Save(outputPath);
            logger.LogInformation(
                "Done. Exported {Total} pages ({Failed} failed) to: {Output}",
                allPages.Count,
                failedPages,
                outputPath);
            return 0;
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex, "Export failed: {Error}", ex.Message);
            return 1;
        }
    }

    public int RunPush(
        string pushPath,
        string notebookName,
        string sectionName,
        string manifestDirectory,
        PushOptions? options = null)
    {
        options ??= new PushOptions();
        try
        {
            var plan = PushPlanBuilder.Build(
                source,
                pushPath,
                notebookName,
                sectionName,
                manifestDirectory,
                options);
            logger.LogInformation(
                "Push plan: {Create} create, {Update} update.",
                plan.Items.Count(item => item.Action == PushAction.Create),
                plan.Items.Count(item => item.Action == PushAction.Update));
            foreach (var item in plan.Items)
                logger.LogInformation("  [{Action}] {Title}", item.Action.ToString().ToUpperInvariant(), item.Title);

            if (options.DryRun)
            {
                logger.LogInformation("Dry run complete. No OneNote Pages were changed.");
                return 0;
            }

            var manifest = SyncManifest.Load(manifestDirectory);
            var succeeded = 0;

            foreach (var item in plan.Items)
            {
                var pageId = item.PageId ?? source.CreatePage(plan.SectionId);
                var pageXml = OneNoteXmlWriter.BuildPageXml(pageId, item.Title, item.Elements);
                try
                {
                    if (item.Action == PushAction.Update)
                        ClearPageOutlines(pageId);
                    source.UpdatePageContent(pageXml);

                    manifest.Pushed[item.MarkdownPath] = new PushEntry
                    {
                        PageId = pageId,
                        NotebookName = notebookName,
                        SectionName = sectionName,
                        LastPushed = DateTime.UtcNow
                    };
                    manifest.Save(manifestDirectory);
                    succeeded++;
                }
                catch (Exception ex)
                {
                    if (item.Action == PushAction.Update && item.OriginalPageXml != null)
                    {
                        try
                        {
                            ClearPageOutlines(pageId);
                            source.UpdatePageContent(PrepareRollbackXml(item.OriginalPageXml));
                            logger.LogWarning("Restored original Page '{Title}' after Push failed.", item.Title);
                        }
                        catch (Exception rollbackException)
                        {
                            throw new AggregateException(
                                $"Push failed for '{item.Title}' and rollback also failed.",
                                ex,
                                rollbackException);
                        }
                    }
                    else if (item.Action == PushAction.Create)
                    {
                        try
                        {
                            source.DeletePage(pageId);
                            logger.LogWarning("Deleted incomplete Page '{Title}' after Push failed.", item.Title);
                        }
                        catch (Exception cleanupException)
                        {
                            throw new AggregateException(
                                $"Push failed for '{item.Title}' and incomplete Page cleanup also failed.",
                                ex,
                                cleanupException);
                        }
                    }

                    throw new InvalidOperationException($"Push failed for '{item.Title}': {ex.Message}", ex);
                }
            }

            manifest.Save(manifestDirectory);
            logger.LogInformation("Push complete. {Count} file(s) pushed.", succeeded);
            return 0;
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex, "Push failed: {Error}", ex.Message);
            return 1;
        }
    }

    private Notebook PopulateNotebook(
        Notebook notebook,
        IReadOnlyCollection<SyncPageAction> pagesToExport,
        out int failedPages)
    {
        failedPages = 0;
        var current = 0;
        var populatedSections = new List<Section>();

        foreach (var (path, section) in notebook.GetAllSections())
        {
            var sectionFullName = string.IsNullOrEmpty(path) ? section.Name : $"{path}/{section.Name}";
            var populatedPages = new List<Page>();

            foreach (var page in section.Pages)
            {
                if (!pagesToExport.Any(action => action.PageId == page.PageId))
                {
                    populatedPages.Add(page);
                    continue;
                }

                logger.LogInformation(
                    "[{Current}/{Total}] Fetching: {PageTitle}",
                    ++current,
                    pagesToExport.Count,
                    page.Title);
                try
                {
                    populatedPages.Add(PageContentParser.ParsePageContent(
                        page,
                        source.GetPageContentXml(page.PageId)));
                }
                catch (Exception ex)
                {
                    failedPages++;
                    logger.LogWarning("Skipping page '{PageTitle}': {Error}", page.Title, ex.Message);
                    populatedPages.Add(page);
                }
            }

            populatedSections.Add(new Section(sectionFullName, populatedPages));
        }

        return new Notebook(notebook.Name, populatedSections);
    }

    private IReadOnlyDictionary<string, string> ExportInkPdfs(Notebook notebook, string outputPath)
    {
        var exported = new Dictionary<string, string>();

        foreach (var (_, section) in notebook.GetAllSections())
        {
            foreach (var page in section.Pages.Where(candidate => candidate.HasInk))
            {
                try
                {
                    var markdownPath = writer.GetPageMarkdownPath(page, section.Name, notebook, outputPath);
                    var pageFolder = Path.GetDirectoryName(markdownPath)!;
                    var pdfBase = Path.GetFileNameWithoutExtension(markdownPath);
                    var attachmentDirectory = Path.Combine(pageFolder, "_attachments");
                    Directory.CreateDirectory(attachmentDirectory);
                    var pdfPath = Path.Combine(attachmentDirectory, $"{pdfBase}.pdf");

                    source.PublishPageToPdf(page.PageId, pdfPath);
                    exported[page.PageId] = pdfPath;

                    if (File.Exists(markdownPath))
                        File.AppendAllText(markdownPath, $"\n\n## Ink / Drawings\n\n![[_attachments/{pdfBase}.pdf]]\n");
                    else
                        logger.LogWarning(
                            "Markdown file not found for ink page '{Page}' at '{Path}'; PDF exported but not embedded.",
                            page.Title,
                            markdownPath);
                }
                catch (Exception ex)
                {
                    logger.LogWarning("Failed to export ink PDF for '{Page}': {Error}", page.Title, ex.Message);
                }
            }
        }

        return exported;
    }

    private void DeletePageFiles(SyncPageEntry entry)
    {
        if (!string.IsNullOrEmpty(entry.ExportedPath) && File.Exists(entry.ExportedPath))
        {
            File.Delete(entry.ExportedPath);
            logger.LogInformation("Deleted: {Path}", entry.ExportedPath);
        }

        foreach (var file in entry.ExportedFiles)
        {
            if (File.Exists(file))
                File.Delete(file);
        }
    }

    private void ClearPageOutlines(string pageId)
    {
        var document = new System.Xml.XmlDocument();
        document.LoadXml(source.GetPageContentXml(pageId));
        var outlines = document.SelectNodes("//*[local-name()='Outline']");
        if (outlines == null)
            return;

        foreach (System.Xml.XmlElement outline in outlines)
        {
            var objectId = outline.GetAttribute("objectID");
            if (!string.IsNullOrEmpty(objectId))
                source.DeletePageContent(pageId, objectId);
        }
    }

    private static string PrepareRollbackXml(string originalPageXml)
    {
        var document = new System.Xml.XmlDocument();
        document.LoadXml(originalPageXml);
        var nodes = document.SelectNodes("//*[@objectID]");
        if (nodes != null)
        {
            foreach (System.Xml.XmlElement node in nodes)
                node.RemoveAttribute("objectID");
        }

        return document.OuterXml;
    }
}
