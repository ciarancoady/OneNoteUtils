using FluentAssertions;
using OneNoteUtils.Core.Parsing;

namespace OneNoteUtils.Core.Tests.Parsing;

public class HierarchyParserTests
{
    private readonly string _hierarchyXml = FixtureHelper.LoadFixture("hierarchy-sample.xml");

    [Fact]
    public void ParseNotebook_FindsByName()
    {
        var notebook = HierarchyParser.ParseNotebook(_hierarchyXml, "Test Notebook", new ExportOptions());

        notebook.Should().NotBeNull();
        notebook!.Name.Should().Be("Test Notebook");
    }

    [Fact]
    public void ParseNotebook_ReturnsNullForUnknownName()
    {
        var notebook = HierarchyParser.ParseNotebook(_hierarchyXml, "Nonexistent", new ExportOptions());

        notebook.Should().BeNull();
    }

    [Fact]
    public void ParseNotebook_FindsByPath()
    {
        var notebook = HierarchyParser.ParseNotebook(_hierarchyXml, @"C:\Notebooks\Test", new ExportOptions());

        notebook.Should().NotBeNull();
        notebook!.Name.Should().Be("Test Notebook");
    }

    [Fact]
    public void ParseNotebook_ParsesAllSections()
    {
        var notebook = HierarchyParser.ParseNotebook(_hierarchyXml, "Test Notebook", new ExportOptions());

        notebook!.Sections.Should().HaveCount(3);
        notebook.Sections[0].Name.Should().Be("Getting Started");
        notebook.Sections[1].Name.Should().Be("Advanced Topics");
        notebook.Sections[2].Name.Should().Be("Archive");
    }

    [Fact]
    public void ParseNotebook_ParsesPages()
    {
        var notebook = HierarchyParser.ParseNotebook(_hierarchyXml, "Test Notebook", new ExportOptions());

        var gettingStarted = notebook!.Sections[0];
        gettingStarted.Pages.Should().HaveCount(3);
        gettingStarted.Pages[0].Title.Should().Be("Welcome Page");
        gettingStarted.Pages[0].Level.Should().Be(1);
        gettingStarted.Pages[1].Title.Should().Be("Sub Page A");
        gettingStarted.Pages[1].Level.Should().Be(2);
    }

    [Fact]
    public void ParseNotebook_ParsesLastModified()
    {
        var notebook = HierarchyParser.ParseNotebook(_hierarchyXml, "Test Notebook", new ExportOptions());

        notebook!.Sections[0].Pages[0].LastModified.Should().NotBeNull();
    }

    [Fact]
    public void ParseNotebook_AppliesSectionFilter()
    {
        var options = new ExportOptions
        {
            SectionFilter = ["Getting Started"]
        };

        var notebook = HierarchyParser.ParseNotebook(_hierarchyXml, "Test Notebook", options);

        notebook!.Sections.Should().HaveCount(1);
        notebook.Sections[0].Name.Should().Be("Getting Started");
    }

    [Fact]
    public void ParseNotebook_AppliesDateFilter()
    {
        var options = new ExportOptions
        {
            DateThreshold = new DateTime(2024, 6, 15)
        };

        var notebook = HierarchyParser.ParseNotebook(_hierarchyXml, "Test Notebook", options);

        // Only pages on or after 2024-06-15 should be included
        var gettingStarted = notebook!.Sections[0];
        gettingStarted.Pages.Should().HaveCount(2); // Welcome Page + Sub Page A
        gettingStarted.Pages.Should().NotContain(p => p.Title == "Sub Page B");
    }

    [Fact]
    public void ParseNotebook_AppliesPageTitleFilter()
    {
        var options = new ExportOptions
        {
            PageFilter = ["sub page a"]
        };

        var notebook = HierarchyParser.ParseNotebook(_hierarchyXml, "Test Notebook", options);

        notebook!.GetAllSections().SelectMany(item => item.Section.Pages)
            .Should().ContainSingle(page => page.Title == "Sub Page A");
    }

    [Fact]
    public void ParseNotebook_AppliesPageIdFilter()
    {
        var unfiltered = HierarchyParser.ParseNotebook(_hierarchyXml, "Test Notebook", new ExportOptions());
        var target = unfiltered!.Sections[0].Pages[0];
        var options = new ExportOptions
        {
            PageFilter = [target.PageId]
        };

        var notebook = HierarchyParser.ParseNotebook(_hierarchyXml, "Test Notebook", options);

        notebook!.GetAllSections().SelectMany(item => item.Section.Pages)
            .Should().ContainSingle(page => page.PageId == target.PageId);
    }

    [Fact]
    public void ParseNotebook_ReturnsNullForEmptyIdentifier()
    {
        var notebook = HierarchyParser.ParseNotebook(_hierarchyXml, "", new ExportOptions());

        notebook.Should().BeNull();
    }
}
