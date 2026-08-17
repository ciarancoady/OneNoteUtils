namespace OneNoteUtils.Core;

/// <summary>
/// Configuration options that control export behaviour.
/// </summary>
public class ExportOptions
{
    /// <summary>
    /// The notebook name or path to export.
    /// </summary>
    public string NotebookIdentifier { get; set; } = "";

    /// <summary>
    /// Root output directory for exported files.
    /// </summary>
    public string OutputPath { get; set; } = "";

    /// <summary>
    /// Optional section filter. Empty means export all sections.
    /// </summary>
    public List<string> SectionFilter { get; set; } = [];

    /// <summary>
    /// Optional page title or page ID filter. Empty means export all pages.
    /// </summary>
    public List<string> PageFilter { get; set; } = [];

    /// <summary>
    /// Only export pages modified on or after this date. Null means no filter.
    /// </summary>
    public DateTime? DateThreshold { get; set; }

    /// <summary>
    /// Whether to include YAML frontmatter in exported Markdown files.
    /// </summary>
    public bool IncludeFrontmatter { get; set; } = true;

    /// <summary>
    /// Use Obsidian-style wiki-links ([[page]]) instead of standard Markdown links.
    /// </summary>
    public bool UseObsidianWikilinks { get; set; } = true;

    /// <summary>
    /// Use ![[image.png]] embed syntax for images.
    /// </summary>
    public bool EmbedImages { get; set; } = true;

    /// <summary>
    /// Use ![[doc.pdf]] embed syntax for PDFs.
    /// </summary>
    public bool EmbedPdfs { get; set; } = false;

    /// <summary>
    /// Use [[SafeName|Original Title]] alias links for page references.
    /// </summary>
    public bool UseAliasLinks { get; set; } = true;

    /// <summary>
    /// Always append a short ID suffix to filenames for uniqueness.
    /// </summary>
    public bool AlwaysSuffixWithShortId { get; set; } = false;

    /// <summary>
    /// Number of characters from OneNote ID to use as suffix.
    /// </summary>
    public int ShortIdLength { get; set; } = 8;
}
