using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using OneNoteUtils.Core;
using OneNoteUtils.OneNote.Interop;

namespace OneNoteUtils.OneNote;

/// <summary>
/// Reads OneNote data via direct COM Interop with a dedicated STA thread.
/// All COM calls are dispatched to the STA thread via a work queue.
/// Requires OneNote desktop (Microsoft 365 / 2016+) to be installed.
/// </summary>
public class ComOneNoteSource : IOneNoteSource, IDisposable
{
    private readonly Thread _staThread;
    private readonly BlockingCollection<Action> _workQueue = new();
    private readonly ManualResetEventSlim _initialized = new(false);
    private readonly ILogger<ComOneNoteSource> _logger;
    private IOneNoteApplication? _app;
    private Exception? _initError;
    private bool _disposed;

    public ComOneNoteSource(ILogger<ComOneNoteSource> logger)
    {
        _logger = logger;

        _staThread = new Thread(StaThreadLoop) { IsBackground = true };
        _staThread.SetApartmentState(ApartmentState.STA);
        _staThread.Start();

        if (!_initialized.Wait(TimeSpan.FromSeconds(10)))
            throw new TimeoutException("OneNote COM initialization timed out.");

        if (_initError != null)
            throw new InvalidOperationException(
                $"Failed to initialize OneNote COM: {_initError.Message}", _initError);

        _logger.LogDebug("ComOneNoteSource initialized (direct COM, STA thread).");
    }

    public string GetHierarchyXml()
    {
        _logger.LogDebug("Fetching hierarchy XML...");
        var result = RunOnStaThread(() =>
        {
            _app!.GetHierarchy("", HierarchyScope.Pages, out var xml);
            return xml;
        });
        _logger.LogDebug("Hierarchy XML received: {Length} characters", result.Length);
        return result;
    }

    public string GetPageContentXml(string pageId)
    {
        return RunOnStaThread(() =>
        {
            _app!.GetPageContent(pageId, out var xml, PageInfo.BinaryData);
            return xml;
        });
    }

    public string CreatePage(string sectionId)
    {
        return RunOnStaThread(() =>
        {
            _app!.CreateNewPage(sectionId, out var pageId);
            return pageId;
        });
    }

    public void DeletePage(string pageId)
    {
        RunOnStaThread(() =>
        {
            _app!.DeleteHierarchy(pageId, 0, true);
            return 0;
        });
    }

    public void UpdatePageContent(string pageXml)
    {
        RunOnStaThread(() =>
        {
            _app!.UpdatePageContent(pageXml, 0, Interop.XMLSchema.Current, true);
            return 0;
        });
    }

    public void DeletePageContent(string pageId, string objectId)
    {
        RunOnStaThread(() =>
        {
            _app!.DeletePageContent(pageId, objectId, 0, true);
            return 0;
        });
    }

    public string? FindSectionId(string notebookName, string sectionName)
    {
        return RunOnStaThread(() =>
        {
            _app!.GetHierarchy("", HierarchyScope.Sections, out var xml);
            var doc = new System.Xml.XmlDocument();
            doc.LoadXml(xml);

            var notebooks = doc.SelectNodes("//*[local-name()='Notebook']");
            if (notebooks == null) return (string?)null;

            foreach (System.Xml.XmlElement nb in notebooks)
            {
                if (!nb.GetAttribute("name").Equals(notebookName, StringComparison.OrdinalIgnoreCase))
                    continue;

                var sections = nb.SelectNodes(".//*[local-name()='Section']");
                if (sections == null) continue;

                foreach (System.Xml.XmlElement sec in sections)
                {
                    if (sec.GetAttribute("name").Equals(sectionName, StringComparison.OrdinalIgnoreCase))
                        return sec.GetAttribute("ID");
                }
            }

            return (string?)null;
        });
    }

    public void PublishPageToPdf(string pageId, string outputFilePath)
    {
        RunOnStaThread(() =>
        {
            // pfPublishFormat = 3 is PDF
            _app!.Publish(pageId, outputFilePath, 3, "");
            return 0;
        });
    }

    private void StaThreadLoop()
    {
        try
        {
            _app = (IOneNoteApplication)new OneNoteApplicationClass();
        }
        catch (Exception ex)
        {
            _initError = ex;
            _initialized.Set();
            return;
        }

        _initialized.Set();

        foreach (var action in _workQueue.GetConsumingEnumerable())
        {
            action();
        }

        // Cleanup COM on the STA thread where it was created
        if (_app != null)
        {
            try { Marshal.ReleaseComObject((object)_app); }
            catch { /* best-effort cleanup */ }
        }
    }

    private T RunOnStaThread<T>(Func<T> func)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        T result = default!;
        Exception? error = null;
        using var done = new ManualResetEventSlim(false);

        _workQueue.Add(() =>
        {
            try
            {
                result = func();
            }
            catch (Exception ex)
            {
                error = ex;
            }
            finally
            {
                done.Set();
            }
        });

        done.Wait();
        if (error != null) throw error;
        return result;
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            _workQueue.CompleteAdding();
            _staThread.Join(TimeSpan.FromSeconds(5));
            _workQueue.Dispose();
            _initialized.Dispose();
        }
        GC.SuppressFinalize(this);
    }
}
