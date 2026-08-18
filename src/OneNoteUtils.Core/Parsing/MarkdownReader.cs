using System.Text.RegularExpressions;
using OneNoteUtils.Core.Models;

namespace OneNoteUtils.Core.Parsing;

/// <summary>
/// Parses Markdown text into a list of ContentElements.
/// Supports headings, paragraphs (bold/italic/strikethrough/links),
/// bullet lists, numbered lists, and tables.
/// </summary>
public static class MarkdownReader
{
    /// <summary>
    /// Extracts the page title from markdown. Uses the first h1 heading if present,
    /// otherwise returns null.
    /// </summary>
    public static string? ExtractTitle(string markdown)
    {
        var stripped = StripFrontmatter(markdown);
        var match = Regex.Match(stripped, @"^#\s+(.+)$", RegexOptions.Multiline);
        return match.Success ? match.Groups[1].Value.Trim() : null;
    }

    /// <summary>
    /// Parses a markdown string into content elements, stripping YAML frontmatter.
    /// </summary>
    /// <param name="markdown">The markdown text to parse.</param>
    /// <param name="basePath">Base directory for resolving relative image paths. Null to skip image loading.</param>
    public static IReadOnlyList<ContentElement> Parse(string markdown, string? basePath = null)
    {
        var lines = StripFrontmatter(markdown).Split('\n')
            .Select(l => l.TrimEnd('\r'))
            .ToList();

        var elements = new List<ContentElement>();
        var i = 0;

        while (i < lines.Count)
        {
            var line = lines[i];

            // Skip blank lines
            if (string.IsNullOrWhiteSpace(line))
            {
                i++;
                continue;
            }

            // Heading
            var headingMatch = Regex.Match(line, @"^(#{1,6})\s+(.+)$");
            if (headingMatch.Success)
            {
                var level = headingMatch.Groups[1].Value.Length;
                var text = headingMatch.Groups[2].Value.Trim();
                elements.Add(new Heading(level, text));
                i++;
                continue;
            }

            // Fenced code block
            if (line.TrimStart().StartsWith("```"))
            {
                var lang = line.TrimStart().Length > 3
                    ? line.TrimStart()[3..].Trim()
                    : null;
                if (string.IsNullOrEmpty(lang)) lang = null;

                i++;
                var codeLines = new List<string>();
                while (i < lines.Count && !lines[i].TrimStart().StartsWith("```"))
                {
                    codeLines.Add(lines[i]);
                    i++;
                }
                if (i < lines.Count) i++; // skip closing ```

                elements.Add(new CodeBlock(string.Join("\n", codeLines), lang));
                continue;
            }

            // Horizontal rule
            if (Regex.IsMatch(line.Trim(), @"^[-*_]{3,}$"))
            {
                elements.Add(new HorizontalRule());
                i++;
                continue;
            }

            // Image — ![[wikilink]] or ![alt](path)
            var wikiImageMatch = Regex.Match(line.Trim(), @"^!\[\[([^\]]+)\]\]$");
            var mdImageMatch = Regex.Match(line.Trim(), @"^!\[([^\]]*)\]\(([^)]+)\)$");
            if (wikiImageMatch.Success)
            {
                var imagePath = ExtractWikiLinkTarget(wikiImageMatch.Groups[1].Value);
                elements.Add(CreateImageFromPath(imagePath, basePath));
                i++;
                continue;
            }
            if (mdImageMatch.Success)
            {
                var imagePath = ExtractMarkdownImageTarget(mdImageMatch.Groups[2].Value);
                elements.Add(CreateImageFromPath(imagePath, basePath));
                i++;
                continue;
            }

            // Table (line contains | and next line is separator)
            if (IsTableRow(line) && i + 1 < lines.Count && IsTableSeparator(lines[i + 1]))
            {
                var tableLines = new List<string> { line };
                i++; // skip header
                i++; // skip separator
                while (i < lines.Count && IsTableRow(lines[i]))
                {
                    tableLines.Add(lines[i]);
                    i++;
                }
                elements.Add(ParseTable(tableLines));
                continue;
            }

            // Bullet list
            if (IsBulletLine(line))
            {
                var (list, nextIndex) = ParseBulletList(lines, i, GetIndent(line));
                elements.Add(list);
                i = nextIndex;
                continue;
            }

            // Numbered list
            if (IsNumberedLine(line))
            {
                var (list, nextIndex) = ParseNumberedList(lines, i, GetIndent(line));
                elements.Add(list);
                i = nextIndex;
                continue;
            }

            // Blockquote
            if (line.TrimStart().StartsWith("> ") || line.TrimStart() == ">")
            {
                var quoteLines = new List<string>();
                while (i < lines.Count && (lines[i].TrimStart().StartsWith("> ") || lines[i].TrimStart() == ">"))
                {
                    var quoteLine = lines[i].TrimStart();
                    quoteLines.Add(quoteLine.Length > 2 ? quoteLine[2..] : "");
                    i++;
                }
                var innerElements = Parse(string.Join("\n", quoteLines), basePath);
                elements.Add(new Blockquote(innerElements));
                continue;
            }

            // Paragraph
            var paragraphText = line;
            i++;
            elements.Add(new Paragraph(ParseInlineFormatting(paragraphText)));
            continue;
        }

        return elements;
    }

    // --- Inline formatting ---

    /// <summary>
    /// Parses inline markdown formatting into Runs.
    /// Handles: **bold**, *italic*, ~~strikethrough~~, [text](url)
    /// </summary>
    public static IReadOnlyList<Run> ParseInlineFormatting(string text)
    {
        if (string.IsNullOrEmpty(text)) return [];

        var runs = new List<Run>();
        var pattern = @"(`([^`]+)`)" +                     // `inline code`
                      @"|(\*\*\*(.+?)\*\*\*)" +           // ***bold italic***
                      @"|(\*\*(.+?)\*\*)" +               // **bold**
                      @"|(\*(.+?)\*)" +                    // *italic*
                      @"|(~~(.+?)~~)" +                    // ~~strikethrough~~
                      @"|(\[([^\]]+)\]\(([^)]+)\))" +     // [text](url)
                      @"|(<u>(.+?)</u>)";                  // <u>underline</u>

        var pos = 0;
        foreach (Match match in Regex.Matches(text, pattern))
        {
            // Add plain text before this match
            if (match.Index > pos)
                runs.Add(new Run(text[pos..match.Index]));

            if (match.Groups[2].Success) // `inline code`
                runs.Add(new Run(match.Groups[2].Value, Code: true));
            else if (match.Groups[4].Success) // ***bold italic***
                runs.Add(new Run(match.Groups[4].Value, Bold: true, Italic: true));
            else if (match.Groups[6].Success) // **bold**
                runs.Add(new Run(match.Groups[6].Value, Bold: true));
            else if (match.Groups[8].Success) // *italic*
                runs.Add(new Run(match.Groups[8].Value, Italic: true));
            else if (match.Groups[10].Success) // ~~strikethrough~~
                runs.Add(new Run(match.Groups[10].Value, Strikethrough: true));
            else if (match.Groups[12].Success) // [text](url)
                runs.Add(new Run(match.Groups[12].Value, HrefUrl: match.Groups[13].Value));
            else if (match.Groups[15].Success) // <u>underline</u>
                runs.Add(new Run(match.Groups[15].Value, Underline: true));

            pos = match.Index + match.Length;
        }

        // Add remaining text
        if (pos < text.Length)
            runs.Add(new Run(text[pos..]));

        return runs.Count > 0 ? runs : [new Run(text)];
    }

    // --- List parsing ---

    private static (BulletList list, int nextIndex) ParseBulletList(List<string> lines, int start, int baseIndent)
    {
        var items = new List<ListItem>();

        var i = start;
        while (i < lines.Count)
        {
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line)) { i++; continue; }

            var indent = GetIndent(line);
            if (indent < baseIndent) break;
            if (indent == baseIndent && !IsBulletLine(line)) break;

            if (indent > baseIndent)
            {
                // Nested list — parse as children of the last item
                if (items.Count > 0)
                {
                    var children = new List<ContentElement>();
                    if (IsBulletLine(line))
                    {
                        var (nested, ni) = ParseBulletList(lines, i, indent);
                        children.Add(nested);
                        i = ni;
                    }
                    else if (IsNumberedLine(line))
                    {
                        var (nested, ni) = ParseNumberedList(lines, i, indent);
                        children.Add(nested);
                        i = ni;
                    }
                    else { i++; continue; }

                    var last = items[^1];
                    items[^1] = last with { Children = (last.Children?.Concat(children).ToList()) ?? children };
                }
                else { i++; }
                continue;
            }

            // This line is a bullet item at our indent level
            var itemText = StripBulletMarker(line);
            items.Add(new ListItem(new List<ContentElement>
            {
                new Paragraph(ParseInlineFormatting(itemText))
            }));
            i++;
        }

        return (new BulletList(items), i);
    }

    private static (NumberedList list, int nextIndex) ParseNumberedList(List<string> lines, int start, int baseIndent)
    {
        var items = new List<ListItem>();

        var i = start;
        while (i < lines.Count)
        {
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line)) { i++; continue; }

            var indent = GetIndent(line);
            if (indent < baseIndent) break;
            if (indent == baseIndent && !IsNumberedLine(line)) break;

            if (indent > baseIndent)
            {
                if (items.Count > 0)
                {
                    var children = new List<ContentElement>();
                    if (IsBulletLine(line))
                    {
                        var (nested, ni) = ParseBulletList(lines, i, indent);
                        children.Add(nested);
                        i = ni;
                    }
                    else if (IsNumberedLine(line))
                    {
                        var (nested, ni) = ParseNumberedList(lines, i, indent);
                        children.Add(nested);
                        i = ni;
                    }
                    else { i++; continue; }

                    var last = items[^1];
                    items[^1] = last with { Children = (last.Children?.Concat(children).ToList()) ?? children };
                }
                else { i++; }
                continue;
            }

            var (numberText, itemText) = StripNumberMarker(line);
            items.Add(new ListItem(new List<ContentElement>
            {
                new Paragraph(ParseInlineFormatting(itemText))
            }, NumberText: numberText));
            i++;
        }

        return (new NumberedList(items), i);
    }

    // --- Table parsing ---

    private static Table ParseTable(List<string> tableLines)
    {
        var rows = new List<TableRow>();

        foreach (var line in tableLines)
        {
            var cells = line.Trim().Trim('|').Split('|')
                .Select(c => new TableCell(new List<ContentElement>
                {
                    new Paragraph(ParseInlineFormatting(c.Trim()))
                }))
                .ToList();

            rows.Add(new TableRow(cells));
        }

        return new Table(rows);
    }

    // --- Helpers ---

    private static string StripFrontmatter(string markdown)
    {
        if (!markdown.TrimStart().StartsWith("---")) return markdown;

        var lines = markdown.Split('\n');
        var i = 0;
        if (lines[i].TrimEnd('\r').Trim() == "---") i++;
        while (i < lines.Length && lines[i].TrimEnd('\r').Trim() != "---") i++;
        if (i < lines.Length) i++; // skip closing ---

        return string.Join('\n', lines[i..]);
    }

    private static bool IsBulletLine(string line)
        => Regex.IsMatch(line.TrimStart(), @"^[-*+]\s");

    private static bool IsNumberedLine(string line)
        => Regex.IsMatch(line.TrimStart(), @"^\d+\.\s");

    private static bool IsTableRow(string line)
        => line.TrimStart().StartsWith('|') && line.TrimEnd().EndsWith('|');

    private static bool IsTableSeparator(string line)
        => Regex.IsMatch(line.Trim(), @"^\|[\s\-:|]+\|$");

    private static int GetIndent(string line)
        => line.Length - line.TrimStart().Length;

    private static string StripBulletMarker(string line)
        => Regex.Replace(line.TrimStart(), @"^[-*+]\s+", "");

    private static (string numberText, string text) StripNumberMarker(string line)
    {
        var match = Regex.Match(line.TrimStart(), @"^(\d+\.)\s+(.*)$");
        return match.Success
            ? (match.Groups[1].Value, match.Groups[2].Value)
            : ("1.", line.TrimStart());
    }

    private static Image CreateImageFromPath(string imagePath, string? basePath)
    {
        imagePath = Uri.UnescapeDataString(imagePath);
        var format = Path.GetExtension(imagePath).TrimStart('.').ToLowerInvariant();
        if (string.IsNullOrEmpty(format)) format = "png";

        var fileName = Path.GetFileName(imagePath);
        var resolvedPath = ResolveImagePath(imagePath, basePath);

        return new Image(
            fileName,
            format,
            () => resolvedPath != null ? File.ReadAllBytes(resolvedPath) : null);
    }

    private static string? ResolveImagePath(string imagePath, string? basePath)
    {
        if (Path.IsPathRooted(imagePath))
            return File.Exists(imagePath) ? imagePath : null;

        if (basePath == null || !Directory.Exists(basePath))
            return null;

        var directCandidates = new[]
        {
            Path.Combine(basePath, imagePath),
            Path.Combine(basePath, "images", imagePath),
            Path.Combine(basePath, "_attachments", imagePath)
        };
        var directMatch = directCandidates.FirstOrDefault(File.Exists);
        if (directMatch != null)
            return directMatch;

        var fileName = Path.GetFileName(imagePath);
        var recursiveMatches = Directory.EnumerateFiles(basePath, fileName, SearchOption.AllDirectories)
            .Take(2)
            .ToList();
        return recursiveMatches.Count == 1 ? recursiveMatches[0] : null;
    }

    private static string ExtractWikiLinkTarget(string target)
    {
        var aliasSeparator = target.IndexOf('|');
        return (aliasSeparator >= 0 ? target[..aliasSeparator] : target).Trim();
    }

    private static string ExtractMarkdownImageTarget(string target)
    {
        target = target.Trim();
        if (target.StartsWith('<') && target.EndsWith('>'))
            return target[1..^1];

        var titleSeparator = Regex.Match(target, """\s+["'(]""");
        return titleSeparator.Success ? target[..titleSeparator.Index] : target;
    }
}
