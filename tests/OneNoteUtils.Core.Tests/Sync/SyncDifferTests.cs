using FluentAssertions;
using OneNoteUtils.Core.Models;
using OneNoteUtils.Core.Sync;

namespace OneNoteUtils.Core.Tests.Sync;

public class SyncDifferTests
{
    [Fact]
    public void Diff_EmptyManifest_AllPagesAreNew()
    {
        var manifest = new SyncManifest();
        var notebook = CreateNotebook(
            ("page-1", "Page One", "Section A", new DateTime(2026, 5, 20)),
            ("page-2", "Page Two", "Section A", new DateTime(2026, 5, 21)));

        var plan = SyncDiffer.Diff(manifest, notebook);

        plan.NewPages.Should().HaveCount(2);
        plan.ModifiedPages.Should().BeEmpty();
        plan.DeletedPages.Should().BeEmpty();
        plan.UnchangedPages.Should().BeEmpty();
    }

    [Fact]
    public void Diff_AllPagesUnchanged_NothingToDo()
    {
        var manifest = new SyncManifest
        {
            Pages =
            {
                ["page-1"] = new SyncPageEntry
                {
                    Title = "Page One",
                    Section = "Section A",
                    LastModified = new DateTime(2026, 5, 20)
                }
            }
        };
        var notebook = CreateNotebook(
            ("page-1", "Page One", "Section A", new DateTime(2026, 5, 20)));

        var plan = SyncDiffer.Diff(manifest, notebook);

        plan.UnchangedPages.Should().HaveCount(1);
        plan.NewPages.Should().BeEmpty();
        plan.ModifiedPages.Should().BeEmpty();
        plan.DeletedPages.Should().BeEmpty();
        plan.TotalWork.Should().Be(0);
    }

    [Fact]
    public void Diff_PageModified_DetectedByTimestamp()
    {
        var manifest = new SyncManifest
        {
            Pages =
            {
                ["page-1"] = new SyncPageEntry
                {
                    Title = "Page One",
                    Section = "Section A",
                    LastModified = new DateTime(2026, 5, 20)
                }
            }
        };
        var notebook = CreateNotebook(
            ("page-1", "Page One", "Section A", new DateTime(2026, 5, 21)));

        var plan = SyncDiffer.Diff(manifest, notebook);

        plan.ModifiedPages.Should().HaveCount(1);
        plan.ModifiedPages[0].PageId.Should().Be("page-1");
        plan.ModifiedPages[0].PreviousEntry.Should().NotBeNull();
    }

    [Fact]
    public void Diff_PageRenamed_DetectedByTitleChange()
    {
        var manifest = new SyncManifest
        {
            Pages =
            {
                ["page-1"] = new SyncPageEntry
                {
                    Title = "Old Title",
                    Section = "Section A",
                    LastModified = new DateTime(2026, 5, 20)
                }
            }
        };
        var notebook = CreateNotebook(
            ("page-1", "New Title", "Section A", new DateTime(2026, 5, 20)));

        var plan = SyncDiffer.Diff(manifest, notebook);

        plan.ModifiedPages.Should().HaveCount(1);
        plan.ModifiedPages[0].Title.Should().Be("New Title");
        plan.ModifiedPages[0].PreviousEntry!.Title.Should().Be("Old Title");
    }

    [Fact]
    public void Diff_PageDeleted_DetectedByMissingFromHierarchy()
    {
        var manifest = new SyncManifest
        {
            Pages =
            {
                ["page-1"] = new SyncPageEntry
                {
                    Title = "Page One",
                    Section = "Section A",
                    LastModified = new DateTime(2026, 5, 20),
                    ExportedPath = @"C:\Export\Page One.md"
                },
                ["page-2"] = new SyncPageEntry
                {
                    Title = "Page Two",
                    Section = "Section A",
                    LastModified = new DateTime(2026, 5, 20)
                }
            }
        };
        // Only page-2 exists now
        var notebook = CreateNotebook(
            ("page-2", "Page Two", "Section A", new DateTime(2026, 5, 20)));

        var plan = SyncDiffer.Diff(manifest, notebook);

        plan.DeletedPages.Should().HaveCount(1);
        plan.DeletedPages[0].PageId.Should().Be("page-1");
        plan.DeletedPages[0].PreviousEntry!.ExportedPath.Should().Contain("Page One.md");
        plan.UnchangedPages.Should().HaveCount(1);
    }

    [Fact]
    public void Diff_MixedChanges()
    {
        var manifest = new SyncManifest
        {
            Pages =
            {
                ["existing-unchanged"] = new SyncPageEntry
                {
                    Title = "Unchanged",
                    Section = "S1",
                    LastModified = new DateTime(2026, 5, 20)
                },
                ["existing-modified"] = new SyncPageEntry
                {
                    Title = "Modified",
                    Section = "S1",
                    LastModified = new DateTime(2026, 5, 19)
                },
                ["deleted-page"] = new SyncPageEntry
                {
                    Title = "Deleted",
                    Section = "S1",
                    LastModified = new DateTime(2026, 5, 18)
                }
            }
        };

        var notebook = CreateNotebook(
            ("existing-unchanged", "Unchanged", "S1", new DateTime(2026, 5, 20)),
            ("existing-modified", "Modified", "S1", new DateTime(2026, 5, 21)),
            ("brand-new", "Brand New", "S1", new DateTime(2026, 5, 21)));

        var plan = SyncDiffer.Diff(manifest, notebook);

        plan.UnchangedPages.Should().HaveCount(1);
        plan.ModifiedPages.Should().HaveCount(1);
        plan.DeletedPages.Should().HaveCount(1);
        plan.NewPages.Should().HaveCount(1);
        plan.TotalWork.Should().Be(3);
    }

    [Fact]
    public void Diff_SameTimestamp_NoModification()
    {
        var manifest = new SyncManifest
        {
            Pages =
            {
                ["page-1"] = new SyncPageEntry
                {
                    Title = "Page",
                    Section = "S1",
                    LastModified = new DateTime(2026, 5, 20, 10, 30, 0)
                }
            }
        };
        var notebook = CreateNotebook(
            ("page-1", "Page", "S1", new DateTime(2026, 5, 20, 10, 30, 0)));

        var plan = SyncDiffer.Diff(manifest, notebook);

        plan.UnchangedPages.Should().HaveCount(1);
        plan.ModifiedPages.Should().BeEmpty();
    }

    [Fact]
    public void Diff_PageMovedToAnotherSection_IsModified()
    {
        var manifest = new SyncManifest
        {
            Pages =
            {
                ["page-1"] = new SyncPageEntry
                {
                    Title = "Page",
                    Section = "Old Section",
                    LastModified = new DateTime(2026, 5, 20)
                }
            }
        };
        var notebook = CreateNotebook(
            ("page-1", "Page", "New Section", new DateTime(2026, 5, 20)));

        var plan = SyncDiffer.Diff(manifest, notebook);

        plan.ModifiedPages.Should().ContainSingle();
        plan.ModifiedPages[0].Section.Should().Be("New Section");
        plan.ModifiedPages[0].PreviousEntry!.Section.Should().Be("Old Section");
    }

    [Fact]
    public void Diff_FilteredSync_DoesNotDeletePagesOutsideFilter()
    {
        var manifest = new SyncManifest
        {
            Pages =
            {
                ["selected"] = new SyncPageEntry { Title = "Selected", Section = "Section A" },
                ["outside-page"] = new SyncPageEntry { Title = "Other", Section = "Section A" },
                ["outside-section"] = new SyncPageEntry { Title = "Selected", Section = "Section B" }
            }
        };
        var notebook = CreateNotebook(("selected", "Selected", "Section A", null));
        var options = new ExportOptions
        {
            SectionFilter = ["Section A"],
            PageFilter = ["Selected"]
        };

        var plan = SyncDiffer.Diff(manifest, notebook, options);

        plan.DeletedPages.Should().BeEmpty();
        plan.UnchangedPages.Should().ContainSingle();
    }

    // --- Helpers ---

    private static Notebook CreateNotebook(params (string id, string title, string section, DateTime? lastMod)[] pages)
    {
        var sections = pages
            .GroupBy(p => p.section)
            .Select(g => new Section(g.Key, g.Select(p =>
                new Page(p.id, p.title, 1, p.lastMod, [])).ToList()))
            .ToList();

        return new Notebook("Test Notebook", sections);
    }
}
