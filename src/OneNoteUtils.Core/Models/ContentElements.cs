namespace OneNoteUtils.Core.Models;

/// <summary>
/// Base type for all content elements in a page.
/// Content elements are ordered and can nest recursively.
/// </summary>
public abstract record ContentElement;

/// <summary>
/// A heading with a level (2+, since page title is h1).
/// </summary>
public record Heading(int Level, string Text) : ContentElement;

/// <summary>
/// A paragraph containing formatted inline text.
/// </summary>
public record Paragraph(IReadOnlyList<Run> Runs) : ContentElement;

/// <summary>
/// A span of inline text with uniform formatting.
/// </summary>
public record Run(
    string Text,
    bool Bold = false,
    bool Italic = false,
    bool Strikethrough = false,
    bool Underline = false,
    bool Code = false,
    bool Highlight = false,
    string? HrefUrl = null);

/// <summary>
/// An unordered (bullet) list.
/// </summary>
public record BulletList(IReadOnlyList<ListItem> Items) : ContentElement;

/// <summary>
/// An ordered (numbered) list.
/// </summary>
public record NumberedList(IReadOnlyList<ListItem> Items) : ContentElement;

/// <summary>
/// A single item in a list, which can contain nested content elements.
/// </summary>
public record ListItem(
    IReadOnlyList<ContentElement> Elements,
    string? NumberText = null,
    IReadOnlyList<ContentElement>? Children = null);

/// <summary>
/// A table with rows and cells.
/// </summary>
public record Table(IReadOnlyList<TableRow> Rows) : ContentElement;

public record TableRow(IReadOnlyList<TableCell> Cells);

public record TableCell(IReadOnlyList<ContentElement> Elements);

/// <summary>
/// An inline image with lazy-loaded binary data.
/// </summary>
public record Image(
    string FileName,
    string Format,
    Func<byte[]?> LoadBytes,
    string? Source = null) : ContentElement;

/// <summary>
/// A file attachment with lazy-loaded binary data.
/// </summary>
public record Attachment(
    string PreferredName,
    Func<byte[]?> LoadBytes) : ContentElement;

/// <summary>
/// A fenced code block with optional language hint.
/// </summary>
public record CodeBlock(string Code, string? Language = null) : ContentElement;

/// <summary>
/// A horizontal rule / thematic break.
/// </summary>
public record HorizontalRule() : ContentElement;

/// <summary>
/// A blockquote containing nested content elements.
/// </summary>
public record Blockquote(IReadOnlyList<ContentElement> Elements) : ContentElement;

/// <summary>
/// A checkbox/to-do item with text content.
/// </summary>
public record Checkbox(bool Checked, IReadOnlyList<Run> Runs) : ContentElement;
