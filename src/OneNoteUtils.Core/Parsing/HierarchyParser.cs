using System.Xml;
using OneNoteUtils.Core.Models;

namespace OneNoteUtils.Core.Parsing;

/// <summary>
/// Parses OneNote hierarchy XML into the domain model (Notebook → Section → Page stubs).
/// Pages are returned without content — use <see cref="PageContentParser"/> to populate elements.
/// </summary>
public static class HierarchyParser
{
    private const string OneNoteNs = "http://schemas.microsoft.com/office/onenote/2013/onenote";

    /// <summary>
    /// Finds a notebook by name or path in the hierarchy XML.
    /// Returns null if not found.
    /// </summary>
    public static Notebook? ParseNotebook(string hierarchyXml, string notebookIdentifier, ExportOptions options)
    {
        var doc = new XmlDocument();
        doc.LoadXml(hierarchyXml);
        var nsMgr = CreateNsMgr(doc);

        var notebookNode = ResolveNotebook(doc, notebookIdentifier);
        if (notebookNode == null) return null;

        var name = notebookNode.GetAttribute("name") ?? notebookIdentifier;
        var notebookId = notebookNode.GetAttribute("ID") ?? "";

        var notebookXmlNode = doc.SelectSingleNode($"//*[local-name()='Notebook' and @ID='{notebookId}']");
        var sections = ParseSections(doc, notebookId, nsMgr, options);
        var sectionGroups = notebookXmlNode != null
            ? ParseSectionGroups(notebookXmlNode, options)
            : (IReadOnlyList<SectionGroup>)[];
        return new Notebook(name, sections, sectionGroups);
    }

    private static XmlElement? ResolveNotebook(XmlDocument doc, string identifier)
    {
        var id = identifier?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(id)) return null;

        var candidatePath = id;
        if (id.EndsWith(".one", StringComparison.OrdinalIgnoreCase))
            candidatePath = Path.GetDirectoryName(id) ?? id;

        var notebooks = doc.SelectNodes("//*[local-name()='Notebook']");
        if (notebooks == null) return null;

        // Match by name first
        foreach (XmlElement nb in notebooks)
        {
            var name = nb.GetAttribute("name");
            if (!string.IsNullOrEmpty(name) &&
                name.Equals(id, StringComparison.OrdinalIgnoreCase))
                return nb;
        }

        // Match by path
        foreach (XmlElement nb in notebooks)
        {
            var path = nb.GetAttribute("path");
            if (!string.IsNullOrEmpty(path) &&
                path.TrimEnd('\\').Equals(candidatePath.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                return nb;
        }

        return null;
    }

    private static IReadOnlyList<Section> ParseSections(
        XmlDocument doc, string notebookId, XmlNamespaceManager nsMgr, ExportOptions options)
    {
        // Only get direct child sections of the notebook (not nested in groups)
        var notebookNode = doc.SelectSingleNode($"//*[local-name()='Notebook' and @ID='{notebookId}']");
        if (notebookNode == null) return [];

        return ParseDirectSections(notebookNode, options);
    }

    private static IReadOnlyList<Section> ParseDirectSections(XmlNode parent, ExportOptions options)
    {
        var sections = new List<Section>();
        var hasSectionFilter = options.SectionFilter.Count > 0;

        foreach (XmlNode child in parent.ChildNodes)
        {
            if (child.LocalName != "Section") continue;
            var sectionName = (child as XmlElement)?.GetAttribute("name") ?? "Untitled";

            if (hasSectionFilter && !options.SectionFilter.Contains(sectionName))
                continue;

            var pages = ParsePages((XmlElement)child, options);
            sections.Add(new Section(sectionName, pages));
        }

        return sections;
    }

    private static IReadOnlyList<SectionGroup> ParseSectionGroups(XmlNode parent, ExportOptions options)
    {
        var groups = new List<SectionGroup>();

        foreach (XmlNode child in parent.ChildNodes)
        {
            if (child.LocalName != "SectionGroup") continue;
            var name = (child as XmlElement)?.GetAttribute("name") ?? "Untitled";

            // Skip OneNote's internal recycle bin section groups
            if (name.StartsWith("OneNote_RecycleBin", StringComparison.OrdinalIgnoreCase))
                continue;

            var sections = ParseDirectSections(child, options);
            var nestedGroups = ParseSectionGroups(child, options);

            // Only include if it has content (after filtering)
            if (sections.Count > 0 || nestedGroups.Count > 0)
                groups.Add(new SectionGroup(name, sections, nestedGroups));
        }

        return groups;
    }

    private static IReadOnlyList<Page> ParsePages(XmlElement sectionNode, ExportOptions options)
    {
        var pageNodes = sectionNode.SelectNodes(".//*[local-name()='Page']");
        if (pageNodes == null) return [];

        var pages = new List<Page>();

        foreach (XmlElement pageNode in pageNodes)
        {
            var pageId = pageNode.GetAttribute("ID");
            if (string.IsNullOrEmpty(pageId)) continue;

            var title = pageNode.GetAttribute("name");
            if (string.IsNullOrWhiteSpace(title)) title = "Untitled";

            if (options.PageFilter.Count > 0 &&
                !options.PageFilter.Any(filter =>
                    filter.Equals(title, StringComparison.OrdinalIgnoreCase) ||
                    filter.Equals(pageId, StringComparison.OrdinalIgnoreCase)))
                continue;

            if (options.DateThreshold.HasValue)
            {
                var lastModStr = pageNode.GetAttribute("lastModifiedTime");
                if (DateTime.TryParse(lastModStr, out var lastMod) && lastMod < options.DateThreshold.Value)
                    continue;
            }

            var levelStr = pageNode.GetAttribute("pageLevel");
            var level = int.TryParse(levelStr, out var l) ? Math.Max(1, l) : 1;

            var lastModifiedStr = pageNode.GetAttribute("lastModifiedTime");
            DateTime? lastModified = DateTime.TryParse(lastModifiedStr, out var dt) ? dt : null;

            // Elements are populated later by PageContentParser
            pages.Add(new Page(pageId, title, level, lastModified, []));
        }

        return pages;
    }

    private static XmlNamespaceManager CreateNsMgr(XmlDocument doc)
    {
        var nsMgr = new XmlNamespaceManager(doc.NameTable);
        nsMgr.AddNamespace("one", OneNoteNs);
        return nsMgr;
    }
}
