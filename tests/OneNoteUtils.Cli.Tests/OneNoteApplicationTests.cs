using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using OneNoteUtils.Cli;
using OneNoteUtils.Core;
using OneNoteUtils.Core.Sync;
using OneNoteUtils.Writers.Obsidian;

namespace OneNoteUtils.Cli.Tests;

public sealed class OneNoteApplicationTests : IDisposable
{
    private readonly string _tempDirectory =
        Path.Combine(Path.GetTempPath(), $"OneNoteUtilsCliTests_{Guid.NewGuid():N}");

    public OneNoteApplicationTests()
    {
        Directory.CreateDirectory(_tempDirectory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
            Directory.Delete(_tempDirectory, recursive: true);
    }

    [Fact]
    public void RunSync_PageMoved_DeletesOldExportAndWritesNewLocation()
    {
        var oldPath = Path.Combine(_tempDirectory, "Notebook", "Old Section", "Page.md");
        Directory.CreateDirectory(Path.GetDirectoryName(oldPath)!);
        File.WriteAllText(oldPath, "old");
        new SyncManifest
        {
            NotebookName = "Notebook",
            Pages =
            {
                ["page-1"] = new SyncPageEntry
                {
                    Title = "Page",
                    Section = "Old Section",
                    LastModified = Timestamp,
                    ExportedPath = oldPath,
                    ExportedFiles = [oldPath]
                }
            }
        }.Save(_tempDirectory);

        var source = new FakeOneNoteSource(Hierarchy("New Section"), PageContent());
        var options = Options();
        var application = CreateApplication(source, options);

        var exitCode = application.RunSync("Notebook", _tempDirectory, options);

        exitCode.Should().Be(0);
        File.Exists(oldPath).Should().BeFalse();
        var manifest = SyncManifest.Load(_tempDirectory);
        manifest.Pages["page-1"].Section.Should().Be("New Section");
        File.Exists(manifest.Pages["page-1"].ExportedPath).Should().BeTrue();
    }

    [Fact]
    public void RunFullExport_WritesPageAndCompleteManifestEntry()
    {
        var source = new FakeOneNoteSource(Hierarchy("Section"), PageContent());
        var options = Options();
        var application = CreateApplication(source, options);

        var exitCode = application.RunFullExport("Notebook", _tempDirectory, options);

        exitCode.Should().Be(0);
        var manifest = SyncManifest.Load(_tempDirectory);
        manifest.Pages.Should().ContainKey("page-1");
        manifest.Pages["page-1"].ExportedPath.Should().NotBeEmpty();
        File.Exists(manifest.Pages["page-1"].ExportedPath).Should().BeTrue();
    }

    [Fact]
    public void RunPush_ReusesTrackedPageAndClearsExistingOutlines()
    {
        var markdownPath = Path.Combine(_tempDirectory, "Handoff.md");
        File.WriteAllText(markdownPath, "# Handoff\n\nUpdated notes.");
        var source = new FakeOneNoteSource(Hierarchy("Shared"), PageContent("outline-1"));
        var application = CreateApplication(source, Options());

        application.RunPush(markdownPath, "Notebook", "Shared", _tempDirectory).Should().Be(0);
        application.RunPush(markdownPath, "Notebook", "Shared", _tempDirectory).Should().Be(0);

        source.CreatePageCalls.Should().Be(1);
        source.UpdatedPages.Should().HaveCount(2);
        source.DeletedObjects.Should().ContainSingle()
            .Which.Should().Be(("page-1", "outline-1"));
        SyncManifest.Load(_tempDirectory).Pushed[Path.GetFullPath(markdownPath)].PageId
            .Should().Be("page-1");
    }

    [Fact]
    public void RunPush_InvalidReplacement_DoesNotClearExistingPage()
    {
        var markdownPath = Path.Combine(_tempDirectory, "Handoff.md");
        File.WriteAllText(markdownPath, "![[missing.png]]");
        new SyncManifest
        {
            Pushed =
            {
                [Path.GetFullPath(markdownPath)] = new PushEntry
                {
                    PageId = "page-1",
                    NotebookName = "Notebook",
                    SectionName = "Shared"
                }
            }
        }.Save(_tempDirectory);
        var source = new FakeOneNoteSource(Hierarchy("Shared"), PageContent("outline-1"));
        var application = CreateApplication(source, Options());

        application.RunPush(markdownPath, "Notebook", "Shared", _tempDirectory).Should().Be(1);

        source.DeletedObjects.Should().BeEmpty();
        source.UpdatedPages.Should().BeEmpty();
    }

    [Fact]
    public void CliArguments_PageOption_IsRepeatable()
    {
        var arguments = CliArguments.Parse(
            ["-n", "Notebook", "-o", "Export", "-p", "First", "--page", "page-2"]);

        arguments.Pages.Should().Equal("First", "page-2");
    }

    private OneNoteApplication CreateApplication(FakeOneNoteSource source, ExportOptions options)
    {
        var writer = new ObsidianMarkdownWriter(options, NullLogger<ObsidianMarkdownWriter>.Instance);
        return new OneNoteApplication(source, writer, NullLogger<OneNoteApplication>.Instance);
    }

    private ExportOptions Options() => new()
    {
        NotebookIdentifier = "Notebook",
        OutputPath = _tempDirectory
    };

    private static readonly DateTime Timestamp =
        new(2026, 8, 17, 10, 0, 0, DateTimeKind.Utc);

    private static string Hierarchy(string sectionName) => $$"""
        <?xml version="1.0"?>
        <one:Notebooks xmlns:one="http://schemas.microsoft.com/office/onenote/2013/onenote">
          <one:Notebook name="Notebook" ID="notebook-1" path="C:\Notebook">
            <one:Section name="{{sectionName}}" ID="section-1">
              <one:Page ID="page-1" name="Page" pageLevel="1" lastModifiedTime="{{Timestamp:O}}" />
            </one:Section>
          </one:Notebook>
        </one:Notebooks>
        """;

    private static string PageContent(string objectId = "") => $$"""
        <?xml version="1.0"?>
        <one:Page xmlns:one="http://schemas.microsoft.com/office/onenote/2013/onenote" ID="page-1" name="Page">
          <one:Title><one:OE><one:T><![CDATA[Page]]></one:T></one:OE></one:Title>
          <one:Outline objectID="{{objectId}}">
            <one:OEChildren>
              <one:OE><one:T><![CDATA[Content]]></one:T></one:OE>
            </one:OEChildren>
          </one:Outline>
        </one:Page>
        """;

    private sealed class FakeOneNoteSource(string hierarchyXml, string pageContentXml) : IOneNoteSource
    {
        public int CreatePageCalls { get; private set; }
        public List<string> UpdatedPages { get; } = [];
        public List<(string PageId, string ObjectId)> DeletedObjects { get; } = [];

        public string GetHierarchyXml() => hierarchyXml;

        public string GetPageContentXml(string pageId) => pageContentXml;

        public string CreatePage(string sectionId)
        {
            CreatePageCalls++;
            return "page-1";
        }

        public void UpdatePageContent(string pageXml) => UpdatedPages.Add(pageXml);

        public void DeletePageContent(string pageId, string objectId) =>
            DeletedObjects.Add((pageId, objectId));

        public string? FindSectionId(string notebookName, string sectionName) => "section-1";

        public void PublishPageToPdf(string pageId, string outputFilePath) =>
            File.WriteAllText(outputFilePath, "pdf");
    }
}
