namespace OneNoteUtils.Core;

/// <summary>
/// Reads and writes data to OneNote. Implementations wrap specific access methods
/// (COM Interop, file parsing, REST API, etc.).
/// </summary>
public interface IOneNoteSource
{
    /// <summary>
    /// Returns the full hierarchy XML for all open notebooks.
    /// </summary>
    string GetHierarchyXml();

    /// <summary>
    /// Returns the page content XML for a specific page, including binary data.
    /// </summary>
    string GetPageContentXml(string pageId);

    /// <summary>
    /// Creates a new blank page in the specified section and returns its page ID.
    /// </summary>
    string CreatePage(string sectionId);

    /// <summary>
    /// Permanently deletes a page by ID.
    /// </summary>
    void DeletePage(string pageId);

    /// <summary>
    /// Updates the content of an existing page using OneNote XML.
    /// </summary>
    void UpdatePageContent(string pageXml);

    /// <summary>
    /// Deletes a content object from a page (e.g., an Outline).
    /// </summary>
    void DeletePageContent(string pageId, string objectId);

    /// <summary>
    /// Finds a section ID by notebook name and section name.
    /// Returns null if not found.
    /// </summary>
    string? FindSectionId(string notebookName, string sectionName);

    /// <summary>
    /// Publishes (exports) a page to a PDF file at the specified path.
    /// </summary>
    void PublishPageToPdf(string pageId, string outputFilePath);
}
