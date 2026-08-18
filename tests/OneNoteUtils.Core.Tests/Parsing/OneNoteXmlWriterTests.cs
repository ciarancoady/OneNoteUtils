using FluentAssertions;
using OneNoteUtils.Core.Models;
using OneNoteUtils.Core.Parsing;
using System.Text.RegularExpressions;

namespace OneNoteUtils.Core.Tests.Parsing;

public class OneNoteXmlWriterTests
{
    [Fact]
    public void BuildPageXml_ContainsPageAndTitle()
    {
        var xml = OneNoteXmlWriter.BuildPageXml("page-1", "Test Page", []);

        xml.Should().Contain("one:Page");
        xml.Should().Contain("ID=\"page-1\"");
        xml.Should().Contain("Test Page");
        xml.Should().Contain("one:Title");
    }

    [Fact]
    public void BuildPageXml_Heading()
    {
        var xml = OneNoteXmlWriter.BuildPageXml("p1", "T", [new Heading(2, "My Heading")]);

        xml.Should().Contain("My Heading");
        xml.Should().Contain("font-weight:bold");
        xml.Should().Contain("font-size:17pt"); // h2 = 17pt
    }

    [Fact]
    public void BuildPageXml_Paragraph()
    {
        var xml = OneNoteXmlWriter.BuildPageXml("p1", "T",
            [new Paragraph([new Run("Hello world")])]);

        xml.Should().Contain("Hello world");
        xml.Should().Contain("one:T");
    }

    [Fact]
    public void BuildPageXml_BoldRun()
    {
        var xml = OneNoteXmlWriter.BuildPageXml("p1", "T",
            [new Paragraph([new Run("bold text", Bold: true)])]);

        xml.Should().Contain("font-weight:bold");
        xml.Should().Contain("bold text");
    }

    [Fact]
    public void BuildPageXml_ItalicRun()
    {
        var xml = OneNoteXmlWriter.BuildPageXml("p1", "T",
            [new Paragraph([new Run("italic", Italic: true)])]);

        xml.Should().Contain("font-style:italic");
    }

    [Fact]
    public void BuildPageXml_LinkRun()
    {
        var xml = OneNoteXmlWriter.BuildPageXml("p1", "T",
            [new Paragraph([new Run("click", HrefUrl: "https://example.com")])]);

        xml.Should().Contain("href=");
        xml.Should().Contain("https://example.com");
        xml.Should().Contain("click");
    }

    [Fact]
    public void BuildPageXml_InlineCodeRun()
    {
        var xml = OneNoteXmlWriter.BuildPageXml("p1", "T",
            [new Paragraph([new Run("code", Code: true)])]);

        xml.Should().Contain("Consolas");
        xml.Should().Contain("code");
    }

    [Fact]
    public void BuildPageXml_BulletList()
    {
        var xml = OneNoteXmlWriter.BuildPageXml("p1", "T",
            [new BulletList([
                new ListItem([new Paragraph([new Run("Item 1")])]),
                new ListItem([new Paragraph([new Run("Item 2")])])
            ])]);

        xml.Should().Contain("one:Bullet");
        xml.Should().Contain("Item 1");
        xml.Should().Contain("Item 2");
    }

    [Fact]
    public void BuildPageXml_NumberedList()
    {
        var xml = OneNoteXmlWriter.BuildPageXml("p1", "T",
            [new NumberedList([
                new ListItem([new Paragraph([new Run("Step 1")])], NumberText: "1."),
                new ListItem([new Paragraph([new Run("Step 2")])], NumberText: "2.")
            ])]);

        xml.Should().Contain("one:Number");
        xml.Should().Contain("Step 1");
        xml.Should().Contain("Step 2");
    }

    [Fact]
    public void BuildPageXml_Table()
    {
        var xml = OneNoteXmlWriter.BuildPageXml("p1", "T",
            [new Table([
                new TableRow([
                    new TableCell([new Paragraph([new Run("A")])]),
                    new TableCell([new Paragraph([new Run("B")])])
                ])
            ])]);

        xml.Should().Contain("one:Table");
        xml.Should().Contain("one:Row");
        xml.Should().Contain("one:Cell");
        xml.Should().Contain("one:Column");
    }

    [Fact]
    public void BuildPageXml_Image()
    {
        var bytes = new byte[] { 0x89, 0x50, 0x4E, 0x47 };
        var xml = OneNoteXmlWriter.BuildPageXml("p1", "T",
            [new Image("test.png", "png", () => bytes)]);

        xml.Should().Contain("one:Image");
        xml.Should().Contain("one:Data");
        xml.Should().Contain("format=\"png\"");
    }

    [Fact]
    public void BuildPageXml_PngImage_IncludesDisplayDimensions()
    {
        var bytes = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAIAAAADCAYAAABWKLW/AAAAAXNSR0IArs4c6QAAAA1JREFUGFdjYGBg+A8AAQQBAH72mAAAAABJRU5ErkJggg==");

        var xml = OneNoteXmlWriter.BuildPageXml(
            "p1",
            "T",
            [new Image("test.png", "png", () => bytes)]);

        xml.Should().Contain("<one:Size width=\"2\" height=\"3\"/>");
    }

    [Fact]
    public void BuildPageXml_ContentAfterImage_IsPreserved()
    {
        var xml = OneNoteXmlWriter.BuildPageXml("p1", "T", [
            new Paragraph([new Run("before")]),
            new Image("test.png", "png", () => [1, 2, 3]),
            new Paragraph([new Run("after")])
        ]);

        xml.Should().Contain("before");
        xml.Should().Contain("one:Image");
        xml.Should().Contain("after");
        xml.IndexOf("after", StringComparison.Ordinal)
            .Should().BeGreaterThan(xml.IndexOf("one:Image", StringComparison.Ordinal));
    }

    [Fact]
    public void BuildPageXml_MissingImage_ThrowsInsteadOfSilentlyDroppingIt()
    {
        var act = () => OneNoteXmlWriter.BuildPageXml(
            "p1",
            "T",
            [new Image("missing.png", "png", () => null)]);

        act.Should().Throw<FileNotFoundException>()
            .WithMessage("*missing.png*");
    }

    [Fact]
    public void BuildPageXml_CodeBlock()
    {
        var xml = OneNoteXmlWriter.BuildPageXml("p1", "T",
            [new CodeBlock("let x = 1;\nlet y = 2;")]);

        xml.Should().NotContain("one:Table");
        xml.Should().Contain("Consolas");
        xml.Should().Contain("background-color:#f0f0f0");
        xml.Should().Contain("let x = 1;");
        xml.Should().Contain("let y = 2;");
    }

    [Fact]
    public void BuildPageXml_CodeBlocksAndMarkdownTable_OnlyEmitsRealTable()
    {
        var xml = OneNoteXmlWriter.BuildPageXml("p1", "T", [
            new CodeBlock("first"),
            new Table([
                new TableRow([
                    new TableCell([new Paragraph([new Run("cell")])])
                ])
            ]),
            new CodeBlock("second")
        ]);

        Regex.Matches(xml, "<one:Table").Should().ContainSingle();
        xml.Should().Contain("first");
        xml.Should().Contain("cell");
        xml.Should().Contain("second");
    }

    [Fact]
    public void BuildPageXml_HorizontalRule()
    {
        var xml = OneNoteXmlWriter.BuildPageXml("p1", "T", [new HorizontalRule()]);

        xml.Should().Contain("━"); // horizontal rule character
    }

    [Fact]
    public void BuildPageXml_Blockquote()
    {
        var xml = OneNoteXmlWriter.BuildPageXml("p1", "T",
            [new Blockquote([new Paragraph([new Run("quoted text")])])]);

        xml.Should().Contain("quoted text");
        xml.Should().Contain("font-style:italic");
    }

    [Fact]
    public void BuildPageXml_SpacingBetweenHeadings()
    {
        var xml = OneNoteXmlWriter.BuildPageXml("p1", "T", [
            new Heading(2, "First"),
            new Paragraph([new Run("text")]),
            new Heading(2, "Second")
        ]);

        // Blank lines should separate headings
        var blankLineCount = xml.Split("CDATA[ ]]").Length - 1;
        blankLineCount.Should().BeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public void BuildPageXml_HtmlEncodesProperly()
    {
        var xml = OneNoteXmlWriter.BuildPageXml("p1", "T",
            [new Paragraph([new Run("A < B & C > D")])]);

        xml.Should().Contain("&lt;");
        xml.Should().Contain("&amp;");
        xml.Should().Contain("&gt;");
        xml.Should().NotContain("< B");
    }
}
