using OneNoteUtils.Core.Models;

namespace OneNoteUtils.Core.Sync;

/// <summary>
/// The result of comparing a SyncManifest against the current OneNote hierarchy.
/// </summary>
public class SyncPlan
{
    /// <summary>Pages not in the manifest — need full export.</summary>
    public List<SyncPageAction> NewPages { get; set; } = [];

    /// <summary>Pages with a newer lastModifiedTime — need re-export.</summary>
    public List<SyncPageAction> ModifiedPages { get; set; } = [];

    /// <summary>Pages in the manifest but no longer in OneNote — need cleanup.</summary>
    public List<SyncPageAction> DeletedPages { get; set; } = [];

    /// <summary>Pages that haven't changed — skip.</summary>
    public List<SyncPageAction> UnchangedPages { get; set; } = [];

    public int TotalWork => NewPages.Count + ModifiedPages.Count + DeletedPages.Count;
}

public class SyncPageAction
{
    public string PageId { get; set; } = "";
    public string Title { get; set; } = "";
    public string Section { get; set; } = "";
    public DateTime? LastModified { get; set; }

    /// <summary>Only set for deleted/modified pages — the previous manifest entry.</summary>
    public SyncPageEntry? PreviousEntry { get; set; }
}

/// <summary>
/// Compares a SyncManifest against the current OneNote hierarchy to produce a SyncPlan.
/// </summary>
public static class SyncDiffer
{
    /// <summary>
    /// Diffs the manifest against the current notebook to determine what needs syncing.
    /// </summary>
    public static SyncPlan Diff(SyncManifest manifest, Notebook notebook, ExportOptions? options = null)
    {
        var plan = new SyncPlan();

        // Build a set of all current page IDs for deletion detection
        var currentPageIds = new HashSet<string>();

        foreach (var (path, section) in notebook.GetAllSections())
        {
            var sectionFullName = string.IsNullOrEmpty(path) ? section.Name : $"{path}/{section.Name}";
            foreach (var page in section.Pages)
            {
                currentPageIds.Add(page.PageId);

                if (!manifest.Pages.TryGetValue(page.PageId, out var entry))
                {
                    plan.NewPages.Add(new SyncPageAction
                    {
                        PageId = page.PageId,
                        Title = page.Title,
                        Section = sectionFullName,
                        LastModified = page.LastModified
                    });
                }
                else if (IsModified(page, sectionFullName, entry))
                {
                    plan.ModifiedPages.Add(new SyncPageAction
                    {
                        PageId = page.PageId,
                        Title = page.Title,
                        Section = sectionFullName,
                        LastModified = page.LastModified,
                        PreviousEntry = entry
                    });
                }
                else
                {
                    // Unchanged
                    plan.UnchangedPages.Add(new SyncPageAction
                    {
                        PageId = page.PageId,
                        Title = page.Title,
                        Section = sectionFullName,
                        LastModified = page.LastModified
                    });
                }
            }
        }

        // Detect deletions — pages in manifest but not in current hierarchy
        foreach (var (pageId, entry) in manifest.Pages)
        {
            if (!currentPageIds.Contains(pageId) && IsInScope(pageId, entry, options))
            {
                plan.DeletedPages.Add(new SyncPageAction
                {
                    PageId = pageId,
                    Title = entry.Title,
                    Section = entry.Section,
                    PreviousEntry = entry
                });
            }
        }

        return plan;
    }

    private static bool IsModified(Page page, string section, SyncPageEntry entry)
    {
        // Title changed (rename)
        if (page.Title != entry.Title)
            return true;

        // Page moved to another section or section group
        if (!section.Equals(entry.Section, StringComparison.OrdinalIgnoreCase))
            return true;

        // Timestamp newer than what we last synced
        if (page.LastModified.HasValue && entry.LastModified.HasValue
            && page.LastModified.Value > entry.LastModified.Value)
            return true;

        // Had no timestamp before but has one now (or vice versa)
        if (page.LastModified.HasValue != entry.LastModified.HasValue)
            return true;

        return false;
    }

    private static bool IsInScope(string pageId, SyncPageEntry entry, ExportOptions? options)
    {
        if (options == null)
            return true;

        if (options.SectionFilter.Count > 0)
        {
            var leafSection = entry.Section.Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? entry.Section;
            if (!options.SectionFilter.Any(filter =>
                    filter.Equals(entry.Section, StringComparison.OrdinalIgnoreCase) ||
                    filter.Equals(leafSection, StringComparison.OrdinalIgnoreCase)))
                return false;
        }

        if (options.PageFilter.Count > 0 &&
            !options.PageFilter.Any(filter =>
                filter.Equals(entry.Title, StringComparison.OrdinalIgnoreCase) ||
                filter.Equals(pageId, StringComparison.OrdinalIgnoreCase)))
            return false;

        return true;
    }
}
