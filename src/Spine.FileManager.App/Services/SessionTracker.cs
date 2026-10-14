using Spine.FileManager.Pages;
using Spine.FileManager.Workspaces;

namespace Spine.FileManager.Services;

/// <summary>
/// The explorers that are open, in window order, saved as the last session half a second after any of them changes,
/// and read back once at start.
/// </summary>
public sealed class SessionTracker(SessionStore store, StorageProviders providers)
{
    private readonly List<ExplorerPageViewModel> _open = [];
    private CancellationTokenSource? _pending;
    private bool _restored;

    public void Register(ExplorerPageViewModel explorer)
    {
        if (!_open.Contains(explorer))
            _open.Add(explorer);
        Changed();
    }

    public void Unregister(ExplorerPageViewModel explorer)
    {
        _open.Remove(explorer);
        Changed();
    }

    public void Changed()
    {
        _pending?.Cancel();
        var pending = _pending = new CancellationTokenSource();
        _ = Task.Delay(500, pending.Token).ContinueWith(_ =>
        {
            if (!pending.IsCancellationRequested)
                MainThread.BeginInvokeOnMainThread(() => store.Save([.. _open.Select(e => e.Snapshot()).Where(w => w.Tabs.Count > 0)]));
        }, TaskScheduler.Default);
    }

    /// <summary>The last session's windows, once (for the first explorer); tabs whose folder is gone are left out.</summary>
    public async Task<IReadOnlyList<WindowState>> RestoreAsync()
    {
        if (_restored)
            return [];
        _restored = true;
        var windows = new List<WindowState>();
        foreach (var window in store.Load())
        {
            var tabs = new List<TabState>();
            foreach (var tab in window.Tabs)
                if (await IsFolder(tab.Location))
                    tabs.Add(tab);
            if (tabs.Count > 0)
                windows.Add(window with { Tabs = tabs, Active = Math.Clamp(window.Active, 0, tabs.Count - 1) });
        }
        return windows;
    }

    private async Task<bool> IsFolder(StorageLocation location)
    {
        try
        {
            return (await providers[location].GetItemAsync(location).WaitAsync(TimeSpan.FromSeconds(2))).IsFolder;
        }
        catch (Exception e) when (e is StorageException or TimeoutException)
        {
            return false;
        }
    }
}
