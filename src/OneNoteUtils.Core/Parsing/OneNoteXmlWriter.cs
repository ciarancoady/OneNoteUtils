using System.Text;
using System.Web;
using OneNoteUtils.Core.Models;

namespace OneNoteUtils.Core.Parsing;

/// <summary>
/// Serializes a list of ContentElements into OneNote page XML
/// suitable for UpdatePageContent.
/// </summary>
public static class OneNoteXmlWriter
{
    private const string OneNoteNs = "http://schemas.microsoft.com/office/onenote/2013/onenote";

    /// <summary>
    /// Creates a full page XML document for UpdatePageContent.
    /// If pageId is provided, updates an existing page; otherwise creates content for a new page.
    /// </summary>
    public static string BuildPageXml(string pageId, string title, IReadOnlyList<ContentElement> elements)
    {
        var sb = new StringBuilder();
        sb.Append($"<?xml version=\"1.0\" encoding=\"utf-8\"?>");
        sb.Append($"<one:Page xmlns:one=\"{OneNoteNs}\" ID=\"{Escape(pageId)}\">");

        // Title
        sb.Append("<one:Title>");
        sb.Append("<one:OE>");
        sb.Append($"<one:T><![CDATA[{HtmlEncode(title)}]]></one:T>");
        sb.Append("</one:OE>");
        sb.Append("</one:Title>");

        // Content in a single Outline
        sb.Append("<one:Outline>");
        sb.Append("<one:OEChildren>");

        for (int i = 0; i < elements.Count; i++)
        {
            // Add a blank line before headings (except the first element)
            if (i > 0 && elements[i] is Heading)
                WriteBlankLine(sb);

            WriteElement(sb, elements[i]);

            // Add a blank line after headings, images, code blocks, tables, and horizontal rules
            if (elements[i] is Heading or Image or CodeBlock or Table or HorizontalRule)
                WriteBlankLine(sb);
        }

        sb.Append("</one:OEChildren>");
        sb.Append("</one:Outline>");
        sb.Append("</one:Page>");

        return sb.ToString();
    }

    private static void WriteBlankLine(StringBuilder sb)
    {
        sb.Append("<one:OE>");
        sb.Append("<one:T><![CDATA[ ]]></one:T>");
        sb.Append("</one:OE>");
    }

    private static void WriteElement(StringBuilder sb, ContentElement element)
    {
        switch (element)
        {
            case Heading heading:
                WriteHeading(sb, heading);
                break;
            case Paragraph paragraph:
                WriteParagraph(sb, paragraph);
                break;
            case BulletList bulletList:
                sb.Append("<one:OE><one:OEChildren>");
                foreach (var item in bulletList.Items)
                    WriteListItem(sb, item, isBullet: true);
                sb.Append("</one:OEChildren></one:OE>");
                break;
            case NumberedList numberedList:
                sb.Append("<one:OE><one:OEChildren>");
                foreach (var item in numberedList.Items)
                    WriteListItem(sb, item, isBullet: false);
                sb.Append("</one:OEChildren></one:OE>");
                break;
            case Table table:
                WriteTable(sb, table);
                break;
            case Image image:
                WriteImage(sb, image);
                break;
            case CodeBlock codeBlock:
                WriteCodeBlock(sb, codeBlock);
                break;
            case HorizontalRule:
                sb.Append("<one:OE>");
                sb.Append("<one:T><![CDATA[<span style=\"font-size:8pt;color:#888888\">━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━</span>]]></one:T>");
                sb.Append("</one:OE>");
                break;
            case Blockquote blockquote:
                WriteBlockquote(sb, blockquote);
                break;
        }
    }

    private static void WriteHeading(StringBuilder sb, Heading heading)
    {
        // OneNote uses QuickStyleDef for headings; for UpdatePageContent
        // we use inline font-size styling to approximate heading levels
        var fontSize = heading.Level switch
        {
            1 => 20,
            2 => 17,
            3 => 14,
            4 => 12,
            _ => 11
        };

        sb.Append("<one:OE>");
        sb.Append($"<one:T><![CDATA[<span style=\"font-size:{fontSize}pt;font-weight:bold\">");
        sb.Append(HtmlEncode(heading.Text));
        sb.Append("</span>]]></one:T>");
        sb.Append("</one:OE>");
    }

    private static void WriteParagraph(StringBuilder sb, Paragraph paragraph)
    {
        sb.Append("<one:OE>");
        sb.Append("<one:T><![CDATA[");
        foreach (var run in paragraph.Runs)
        {
            WriteRun(sb, run);
        }
        sb.Append("]]></one:T>");
        sb.Append("</one:OE>");
    }

    private static void WriteImage(StringBuilder sb, Image image)
    {
        var bytes = image.LoadBytes();
        if (bytes == null)
            throw new FileNotFoundException($"Image '{image.FileName}' could not be resolved.");

        var base64 = Convert.ToBase64String(bytes);
        var format = image.Format.ToLowerInvariant();
        if (string.IsNullOrEmpty(format)) format = "png";

        sb.Append("<one:OE>");
        sb.Append($"<one:Image format=\"{format}\">");
        if (TryGetImageDimensions(bytes, out var width, out var height))
            sb.Append($"<one:Size width=\"{width}\" height=\"{height}\"/>");
        sb.Append($"<one:Data>{base64}</one:Data>");
        sb.Append("</one:Image>");
        sb.Append("</one:OE>");
    }

    private static bool TryGetImageDimensions(byte[] bytes, out int width, out int height)
    {
        width = 0;
        height = 0;

        if (bytes.Length >= 24 &&
            bytes[0] == 0x89 &&
            bytes[1] == 0x50 &&
            bytes[2] == 0x4E &&
            bytes[3] == 0x47)
        {
            width = ReadBigEndianInt32(bytes, 16);
            height = ReadBigEndianInt32(bytes, 20);
            return width > 0 && height > 0;
        }

        if (bytes.Length >= 10 &&
            bytes[0] == 0x47 &&
            bytes[1] == 0x49 &&
            bytes[2] == 0x46)
        {
            width = bytes[6] | bytes[7] << 8;
            height = bytes[8] | bytes[9] << 8;
            return width > 0 && height > 0;
        }

        if (bytes.Length >= 26 && bytes[0] == 0x42 && bytes[1] == 0x4D)
        {
            width = BitConverter.ToInt32(bytes, 18);
            height = Math.Abs(BitConverter.ToInt32(bytes, 22));
            return width > 0 && height > 0;
        }

        return TryGetJpegDimensions(bytes, out width, out height);
    }

    private static bool TryGetJpegDimensions(byte[] bytes, out int width, out int height)
    {
        width = 0;
        height = 0;
        if (bytes.Length < 4 || bytes[0] != 0xFF || bytes[1] != 0xD8)
            return false;

        var offset = 2;
        while (offset + 8 < bytes.Length)
        {
            if (bytes[offset] != 0xFF)
            {
                offset++;
                continue;
            }

            var marker = bytes[offset + 1];
            if (marker is >= 0xC0 and <= 0xC3)
            {
                height = bytes[offset + 5] << 8 | bytes[offset + 6];
                width = bytes[offset + 7] << 8 | bytes[offset + 8];
                return width > 0 && height > 0;
            }

            if (offset + 3 >= bytes.Length)
                return false;

            var segmentLength = bytes[offset + 2] << 8 | bytes[offset + 3];
            if (segmentLength < 2)
                return false;

            offset += segmentLength + 2;
        }

        return false;
    }

    private static int ReadBigEndianInt32(byte[] bytes, int offset) =>
        bytes[offset] << 24 |
        bytes[offset + 1] << 16 |
        bytes[offset + 2] << 8 |
        bytes[offset + 3];

    private static void WriteCodeBlock(StringBuilder sb, CodeBlock codeBlock)
    {
        var codeLines = codeBlock.Code.Split('\n');

        sb.Append("<one:OE>");
        sb.Append("<one:OEChildren>");

        foreach (var line in codeLines)
        {
            sb.Append("<one:OE>");
            sb.Append("<one:T><![CDATA[<span style=\"font-family:Consolas,monospace;font-size:10pt;background-color:#f0f0f0\">");
            sb.Append(HtmlEncode(string.IsNullOrEmpty(line) ? " " : line));
            sb.Append("</span>]]></one:T>");
            sb.Append("</one:OE>");
        }

        sb.Append("</one:OEChildren>");
        sb.Append("</one:OE>");
    }

    private static void WriteBlockquote(StringBuilder sb, Blockquote blockquote)
    {
        // Render blockquote as indented italic text
        foreach (var element in blockquote.Elements)
        {
            if (element is Paragraph p)
            {
                sb.Append("<one:OE>");
                sb.Append("<one:T><![CDATA[<span style=\"font-style:italic;color:#666666\">▎ ");
                foreach (var run in p.Runs)
                    WriteRun(sb, run);
                sb.Append("</span>]]></one:T>");
                sb.Append("</one:OE>");
            }
            else
            {
                WriteElement(sb, element);
            }
        }
    }

    private static void WriteRun(StringBuilder sb, Run run)
    {
        var text = HtmlEncode(run.Text);
        var hasStyle = run.Bold || run.Italic || run.Strikethrough || run.Underline;

        if (run.Code)
        {
            sb.Append($"<span style=\"font-family:Consolas,monospace;background-color:#f0f0f0\">{text}</span>");
            return;
        }

        if (run.HrefUrl != null)
        {
            var inner = text;
            if (hasStyle) inner = WrapWithStyles(inner, run);
            sb.Append($"<a href=\"{HtmlEncode(run.HrefUrl)}\">{inner}</a>");
            return;
        }

        if (hasStyle)
        {
            sb.Append(WrapWithStyles(text, run));
            return;
        }

        sb.Append(text);
    }

    private static string WrapWithStyles(string text, Run run)
    {
        var styles = new List<string>();
        if (run.Bold) styles.Add("font-weight:bold");
        if (run.Italic) styles.Add("font-style:italic");
        if (run.Strikethrough) styles.Add("text-decoration:line-through");
        if (run.Underline) styles.Add("text-decoration:underline");

        return $"<span style=\"{string.Join(";", styles)}\">{text}</span>";
    }

    private static void WriteListItem(StringBuilder sb, ListItem item, bool isBullet)
    {
        sb.Append("<one:OE>");

        // List marker
        sb.Append("<one:List>");
        if (isBullet)
            sb.Append("<one:Bullet bullet=\"2\" fontSize=\"11.0\"/>");
        else
            sb.Append("<one:Number numberSequence=\"0\" numberFormat=\"##.\" fontSize=\"11.0\"/>");
        sb.Append("</one:List>");

        // Item text
        sb.Append("<one:T><![CDATA[");
        foreach (var element in item.Elements)
        {
            if (element is Paragraph p)
            {
                foreach (var run in p.Runs)
                    WriteRun(sb, run);
            }
        }
        sb.Append("]]></one:T>");

        // Nested children
        if (item.Children is { Count: > 0 })
        {
            sb.Append("<one:OEChildren>");
            foreach (var child in item.Children)
            {
                WriteElement(sb, child);
            }
            sb.Append("</one:OEChildren>");
        }

        sb.Append("</one:OE>");
    }

    private static void WriteTable(StringBuilder sb, Table table)
    {
        if (table.Rows.Count == 0) return;

        var colCount = table.Rows.Max(r => r.Cells.Count);

        sb.Append("<one:OE>");
        sb.Append("<one:Table bordersVisible=\"true\">");

        // Columns
        sb.Append("<one:Columns>");
        for (int c = 0; c < colCount; c++)
            sb.Append($"<one:Column index=\"{c}\" width=\"120\"/>");
        sb.Append("</one:Columns>");

        // Rows
        foreach (var row in table.Rows)
        {
            sb.Append("<one:Row>");
            for (int c = 0; c < colCount; c++)
            {
                sb.Append("<one:Cell>");
                sb.Append("<one:OEChildren>");
                if (c < row.Cells.Count)
                {
                    foreach (var element in row.Cells[c].Elements)
                    {
                        if (element is Paragraph p)
                            WriteParagraph(sb, p);
                        else
                            sb.Append("<one:OE><one:T><![CDATA[]]></one:T></one:OE>");
                    }
                }
                else
                {
                    sb.Append("<one:OE><one:T><![CDATA[]]></one:T></one:OE>");
                }
                sb.Append("</one:OEChildren>");
                sb.Append("</one:Cell>");
            }
            sb.Append("</one:Row>");
        }

        sb.Append("</one:Table>");
        sb.Append("</one:OE>");
    }

    private static string HtmlEncode(string text) => HttpUtility.HtmlEncode(text);
    private static string Escape(string text) => HttpUtility.HtmlAttributeEncode(text);
}
