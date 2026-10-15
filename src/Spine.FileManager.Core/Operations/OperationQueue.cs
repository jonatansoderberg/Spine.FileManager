using System.Diagnostics;
using Spine.FileManager.Storage;

namespace Spine.FileManager.Operations;

/// <summary>
/// Runs copies and moves one at a time, in queue order, in the background; asks <see cref="ResolveConflict"/> when a
/// name is taken; reports each operation through <see cref="Changed"/> at most <see cref="ReportsPerSecond"/> times a
/// second; and, with a journal, keeps every operation across a crash so it can be resumed where it stopped.
/// </summary>
public sealed class OperationQueue
{
    // Up to this size a file is copied by the provider itself (APFS clones it); above it through a stream, so a large
    // file shows its bytes and can be cancelled midway. Spike 3 decides this properly (TransferPlanner).
    public const long NativeCopyLimit = 16 * 1024 * 1024;

    /// <summary>A file being streamed is written under this name, hidden, and renamed when complete (plan §27).</summary>
    public static string PartName(string name) => $".{name}.spine-part";

    private const int BufferSize = 1024 * 1024;
    private readonly StorageProviders _providers;
    private readonly OperationJournal? _journal;
    private readonly List<FileOperation> _operations = [];
    private readonly SemaphoreSlim _work = new(0);
    private int _workerStarted;

    public OperationQueue(StorageProviders providers, OperationJournal? journal = null)
    {
        _providers = providers;
        _journal = journal;
        if (journal is not null)
            _operations.AddRange(journal.Load());
    }

    public int ReportsPerSecond { get; init; } = 10;

    /// <summary>Asked for every taken name, unless an earlier answer was "for all". Without it a conflict keeps both.</summary>
    public Func<FileOperation, Conflict, Task<ConflictAnswer>>? ResolveConflict { get; set; }

    /// <summary>
    /// Runs a <see cref="OperationKind.Send"/>: the operation, a progress callback (bytes done, bytes in all), a
    /// checkpoint to await between pieces (a paused operation waits there) and the cancellation. Set by the app, which
    /// knows the linked devices; a send without it fails.
    /// </summary>
    public Func<FileOperation, Action<long, long>, Func<CancellationToken, Task>, CancellationToken, Task>? Sender { get; set; }

    /// <summary>Raised on a background thread for state changes and, throttled, for progress.</summary>
    public event Action<FileOperation>? Changed;

    /// <summary>Every operation the queue knows, oldest first: running, queued, interrupted and the recent history.</summary>
    public IReadOnlyList<FileOperation> Operations
    {
        get
        {
            lock (_operations)
                return [.. _operations];
        }
    }

    public FileOperation Enqueue(OperationKind kind, IEnumerable<StorageLocation> sources, StorageLocation target)
    {
        var operation = new FileOperation(Guid.NewGuid(), kind, [.. sources], target, DateTimeOffset.Now);
        lock (_operations)
            _operations.Add(operation);
        Notify(operation, save: true);
        Start();
        return operation;
    }

    /// <summary>Runs an interrupted, failed or cancelled operation again; what it finished before is skipped.</summary>
    public void Resume(FileOperation operation)
    {
        if (!operation.CanResume)
            return;
        operation.Cancellation = new CancellationTokenSource();
        operation.Running.Set();
        operation.Errors.Clear();
        operation.State = OperationState.Queued;
        lock (_operations)
        {
            // To the end of the queue, behind what was waiting already.
            _operations.Remove(operation);
            _operations.Add(operation);
        }
        Notify(operation, save: true);
        Start();
    }

    public void Pause(FileOperation operation)
    {
        if (operation.State != OperationState.Running)
            return;
        operation.Running.Reset();
        operation.State = OperationState.Paused;
        Notify(operation, save: true);
    }

    public void Continue(FileOperation operation)
    {
        if (operation.State != OperationState.Paused)
            return;
        operation.State = OperationState.Running;
        operation.Running.Set();
        Notify(operation, save: true);
    }

    /// <summary>Moves a queued operation one place earlier (-1) or later (+1) among the queued ones.</summary>
    public void Move(FileOperation operation, int direction)
    {
        lock (_operations)
        {
            var queued = _operations.Where(o => o.State == OperationState.Queued).ToList();
            var at = queued.IndexOf(operation);
            var to = at + direction;
            if (at < 0 || to < 0 || to >= queued.Count)
                return;
            var other = queued[to];
            int i = _operations.IndexOf(operation), j = _operations.IndexOf(other);
            (_operations[i], _operations[j]) = (_operations[j], _operations[i]);
        }
        Notify(operation, save: false);
    }

    /// <summary>Drops a finished or interrupted operation from the history and the journal.</summary>
    public void Remove(FileOperation operation)
    {
        if (!operation.IsFinished && operation.State != OperationState.Interrupted)
            return;
        lock (_operations)
            _operations.Remove(operation);
        _journal?.Delete(operation.Id);
        Changed?.Invoke(operation);
    }

    /// <summary>Waits until <paramref name="operation"/> has finished, for tests and callers that need the result.</summary>
    public async Task WaitAsync(FileOperation operation)
    {
        while (!operation.IsFinished && operation.State != OperationState.Interrupted)
            await Task.Delay(10);
    }

    private void Start()
    {
        _work.Release();
        if (Interlocked.Exchange(ref _workerStarted, 1) == 0)
            _ = Task.Run(Worker);
    }

    private async Task Worker()
    {
        while (true)
        {
            await _work.WaitAsync();
            FileOperation? next;
            lock (_operations)
                next = _operations.FirstOrDefault(o => o.State == OperationState.Queued);
            if (next is not null)
                await Execute(next);
        }
    }

    private async Task Execute(FileOperation operation)
    {
        operation.Attempts++;
        try
        {
            if (operation.Kind == OperationKind.Send)
                await SendAsync(operation);
            else
                await new Execution(this, operation).Run();
        }
        catch (DeviceLink.TransferDeclinedException e)
        {
            // The receiver said no: kept as cancelled with its reason, and not tried again by itself.
            operation.Errors.Add(new(operation.Target, e.Message));
            operation.State = OperationState.Cancelled;
        }
        catch (OperationCanceledException)
        {
            operation.State = OperationState.Cancelled;
        }
        catch (Exception e)
        {
            operation.Errors.Add(new(operation.Target, e.Message));
            operation.State = OperationState.Failed;
        }
        finally
        {
            Notify(operation, save: true);
            // Another queued operation may be waiting for this one.
            _work.Release();
        }
    }

    // A send is one piece of work for the device link: the queue keeps its state, journal, pause and progress.
    private async Task SendAsync(FileOperation operation)
    {
        if (Sender is not { } send)
            throw new InvalidOperationException("Sending to a device is not set up.");
        var cancel = operation.Cancellation.Token;
        operation.State = OperationState.Running;
        Notify(operation, save: true);
        var clock = Stopwatch.StartNew();
        long lastReport = 0, lastSave = 0, startBytes = -1;
        await send(operation, (done, total) =>
        {
            if (startBytes < 0)
                startBytes = done;
            var seconds = clock.Elapsed.TotalSeconds;
            operation.Progress = new OperationProgress(done, total, done >= total ? operation.Sources.Count : 0, operation.Sources.Count,
                operation.Target.DisplayPath, seconds > 0.5 ? (done - startBytes) / seconds : 0);
            var now = clock.ElapsedMilliseconds;
            if (now - lastReport >= 1000 / ReportsPerSecond)
            {
                lastReport = now;
                var save = now - lastSave >= 1000;
                if (save)
                    lastSave = now;
                Notify(operation, save);
            }
        }, async checkpointCancel =>
        {
            while (!operation.Running.IsSet)
                await Task.Delay(100, checkpointCancel);
        }, cancel);
        operation.State = OperationState.Completed;
    }

    private void Notify(FileOperation operation, bool save)
    {
        operation.Updated = DateTimeOffset.Now;
        // An operation counts as finished a moment before its last report; one removed in between stays removed.
        bool known;
        lock (_operations)
            known = _operations.Contains(operation);
        if (save && known)
            _journal?.Save(operation);
        Changed?.Invoke(operation);
    }

    /// <summary>The state of one operation while it runs.</summary>
    private sealed class Execution(OperationQueue queue, FileOperation operation)
    {
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private readonly CancellationToken _cancel = operation.Cancellation.Token;
        private readonly bool _resuming = operation.Attempts > 1 || operation.Done.Count > 0;
        // Items and bytes under each counted item, so a skipped or natively moved folder adds its share without being
        // listed again (after a move it is no longer there to list).
        private readonly Dictionary<StorageLocation, (int Items, long Bytes)> _subtree = [];
        private long _bytesTotal, _bytesDone;
        private int _itemsTotal, _itemsDone;
        private string? _current;
        private long _lastReport, _lastSave;
        private ConflictAnswer? _forAll;

        public async Task Run()
        {
            operation.State = OperationState.Preparing;
            queue.Notify(operation, save: true);
            var targetProvider = queue._providers[operation.Target];

            // A file half written when the app stopped is removed and written again.
            if (operation.Writing is { } partial)
            {
                try { await targetProvider.DeleteAsync(partial, StorageDeleteMode.Permanent, _cancel); } catch (StorageException) { }
                operation.Writing = null;
            }

            var sources = new List<StorageItem>();
            foreach (var source in operation.Sources)
            {
                StorageItem item;
                try
                {
                    item = await queue._providers[source].GetItemAsync(source, _cancel);
                }
                catch (StorageException e) when (e.Kind == StorageErrorKind.NotFound && _resuming && operation.Kind == OperationKind.Move)
                {
                    // Moved before the interruption: its source is gone and its result is already in place.
                    continue;
                }
                if (item.IsFolder && IsInside(operation.Target, source))
                    throw new StorageException(StorageErrorKind.NotSupported, source, $"“{item.Name}” cannot be copied or moved into itself.");
                sources.Add(item);
                await Count(item);
            }

            operation.State = OperationState.Running;
            Report(force: true);
            foreach (var item in sources)
            {
                await Checkpoint();
                // Copied into its own folder, an item becomes its copy ("name (2)"), as Finder's Duplicate does.
                var sameFolder = queue._providers[item.Location].GetParent(item.Location)?.Equals(operation.Target) == true;
                if (sameFolder && operation.Kind == OperationKind.Move)
                {
                    operation.Results.Add(item.Location);
                    Skip(item);
                    continue;
                }
                if (await Transfer(item, operation.Target, targetProvider, keepBoth: sameFolder && !_resuming) is { } result && !operation.Results.Contains(result))
                {
                    operation.Results.Add(result);
                    operation.Placed.Add(new(item.Location, result));
                }
            }
            Report(force: true);
            operation.State = operation.Errors.Count > 0 && operation.Results.Count == 0 ? OperationState.Failed : OperationState.Completed;
        }

        private async Task<(int Items, long Bytes)> Count(StorageItem item)
        {
            var total = (Items: 1, Bytes: item.IsFolder ? 0L : item.Size ?? 0);
            if (item.IsFolder)
                await foreach (var child in queue._providers[item.Location].EnumerateAsync(item.Location, _cancel))
                {
                    var (items, bytes) = await Count(child);
                    total = (total.Items + items, total.Bytes + bytes);
                }
            _subtree[item.Location] = total;
            _itemsTotal++;
            _bytesTotal += item.IsFolder ? 0 : item.Size ?? 0;
            return total;
        }

        /// <summary>Copies or moves <paramref name="item"/> into <paramref name="folder"/>; returns where it ended up, or null when skipped.</summary>
        private async Task<StorageLocation?> Transfer(StorageItem item, StorageLocation folder, IStorageProvider target, bool keepBoth = false)
        {
            await Checkpoint();
            _current = item.Name;
            var source = queue._providers[item.Location];
            var name = item.Name;
            var existing = await Existing(target, folder, name);

            if (existing is not null && IsDone(existing.Location))
            {
                // Finished before an interruption.
                Skip(item);
                return existing.Location;
            }

            var replace = false;
            var merge = false;
            if (existing is not null)
            {
                var ours = existing.IsFolder && item.IsFolder && IsCreated(existing.Location);
                var choice = ours ? ConflictChoice.Replace : keepBoth ? ConflictChoice.KeepBoth : (await Ask(item, existing)).Choice;
                switch (choice)
                {
                    case ConflictChoice.Stop:
                        operation.Cancel();
                        _cancel.ThrowIfCancellationRequested();
                        break;
                    case ConflictChoice.Skip:
                        Skip(item);
                        return null;
                    case ConflictChoice.KeepBoth:
                        name = await FreeName(target, folder, name);
                        break;
                    case ConflictChoice.Replace when item.IsFolder && existing.IsFolder:
                        // A folder onto a folder merges, as Explorer does; its files meet their own conflicts.
                        merge = true;
                        break;
                    case ConflictChoice.Replace:
                        replace = true;
                        break;
                }
            }

            try
            {
                var sameProvider = source == target;
                var capabilities = await target.GetCapabilitiesAsync(folder, _cancel);

                if (operation.Kind == OperationKind.Move && sameProvider && capabilities.HasFlag(StorageCapabilities.NativeMove) && !merge)
                {
                    if (replace)
                        await target.DeleteAsync(existing!.Location, StorageDeleteMode.Permanent, _cancel);
                    var moved = await target.MoveAsync(item.Location, folder, name, _cancel);
                    MarkDone(moved.Location);
                    Skip(item);
                    return moved.Location;
                }

                StorageLocation result;
                if (item.IsFolder)
                {
                    if (merge)
                        result = existing!.Location;
                    else
                    {
                        result = (await target.CreateFolderAsync(folder, name, _cancel)).Location;
                        lock (operation.Done)
                            operation.CreatedFolders.Add(result.ItemId);
                    }
                    _itemsDone++;
                    Report();
                    var children = new List<StorageItem>();
                    await foreach (var child in source.EnumerateAsync(item.Location, _cancel))
                        children.Add(child);
                    foreach (var child in children)
                        await Transfer(child, result, target);
                }
                else
                {
                    var native = sameProvider && capabilities.HasFlag(StorageCapabilities.NativeCopy) && (item.Size ?? 0) <= NativeCopyLimit;
                    result = native
                        ? await NativeCopy(item, target, folder, name, replace ? existing : null)
                        : await StreamCopy(item, source, target, folder, name, replace ? existing : null);
                    _bytesDone += item.Size ?? 0;
                    _itemsDone++;
                    Report();
                }

                if (operation.Kind == OperationKind.Move)
                    await source.DeleteAsync(item.Location, StorageDeleteMode.Permanent, _cancel);
                MarkDone(result);
                return result;
            }
            catch (StorageException e)
            {
                operation.Errors.Add(new(item.Location, e.Message));
                Skip(item);
                return null;
            }
        }

        private async Task<StorageLocation> NativeCopy(StorageItem item, IStorageProvider target, StorageLocation folder, string name, StorageItem? replacing)
        {
            if (replacing is not null)
                await target.DeleteAsync(replacing.Location, StorageDeleteMode.Permanent, _cancel);
            var expected = (await Existing(target, folder, name))?.Location;
            await Writing(expected ?? Child(target, folder, name));
            var copied = await target.CopyAsync(item.Location, folder, name, _cancel);
            await Writing(null);
            return copied.Location;
        }

        // Written under a hidden part name and renamed when complete, so an interrupted copy never looks like a whole file.
        private async Task<StorageLocation> StreamCopy(StorageItem item, IStorageProvider source, IStorageProvider target, StorageLocation folder, string name, StorageItem? replacing)
        {
            var part = PartName(name);
            if (await Existing(target, folder, part) is { } stale)
                await target.DeleteAsync(stale.Location, StorageDeleteMode.Permanent, _cancel);
            await Writing(Child(target, folder, part));
            var start = _bytesDone;
            try
            {
                await using var input = await source.OpenReadAsync(item.Location, _cancel);
                await using var output = await target.OpenWriteAsync(folder, part, item.Size, overwrite: false, _cancel);
                var buffer = new byte[BufferSize];
                int read;
                while ((read = await input.ReadAsync(buffer, _cancel)) > 0)
                {
                    await output.WriteAsync(buffer.AsMemory(0, read), _cancel);
                    _bytesDone += read;
                    Report();
                    await Checkpoint();
                }
            }
            catch (OperationCanceledException)
            {
                // Cancelled on purpose: the half-written part goes. A crash leaves it for the resume to remove.
                if (await Existing(target, folder, part, CancellationToken.None) is { } half)
                    await target.DeleteAsync(half.Location, StorageDeleteMode.Permanent, CancellationToken.None);
                throw;
            }
            if (replacing is not null)
                await target.DeleteAsync(replacing.Location, StorageDeleteMode.Permanent, _cancel);
            var written = (await Existing(target, folder, part))!.Location;
            var renamed = await target.RenameAsync(written, name, _cancel);
            await Writing(null);
            // The caller adds the whole file once it is done.
            _bytesDone = start;
            return renamed.Location;
        }

        private static StorageLocation Child(IStorageProvider provider, StorageLocation folder, string name) =>
            // Only the file-system provider's ids are paths; others report their file once it exists.
            new(provider.Id, Path.Combine(folder.ItemId, name), Path.Combine(folder.DisplayPath, name), folder.NativePath is { } native ? Path.Combine(native, name) : null);

        private async Task Writing(StorageLocation? location)
        {
            operation.Writing = location;
            if (location is not null)
                await Save(force: true);
        }

        private bool IsDone(StorageLocation location)
        {
            lock (operation.Done)
                return operation.Done.Contains(location.ItemId);
        }

        private bool IsCreated(StorageLocation location)
        {
            lock (operation.Done)
                return operation.CreatedFolders.Contains(location.ItemId);
        }

        private void MarkDone(StorageLocation location)
        {
            lock (operation.Done)
                operation.Done.Add(location.ItemId);
        }

        private void Skip(StorageItem item)
        {
            var (items, bytes) = _subtree.TryGetValue(item.Location, out var counted) ? counted : (1, item.Size ?? 0);
            _itemsDone += items;
            _bytesDone += bytes;
            Report();
        }

        /// <summary>Stops here while paused and throws when cancelled; saves the journal now and then.</summary>
        private async Task Checkpoint()
        {
            _cancel.ThrowIfCancellationRequested();
            if (!operation.Running.IsSet)
            {
                await Save(force: true);
                await Task.Run(() => operation.Running.Wait(_cancel), _cancel);
            }
            await Save();
        }

        private Task Save(bool force = false)
        {
            var now = _clock.ElapsedMilliseconds;
            if (force || now - _lastSave >= 1000)
            {
                _lastSave = now;
                queue.Notify(operation, save: true);
            }
            return Task.CompletedTask;
        }

        private async Task<ConflictAnswer> Ask(StorageItem item, StorageItem existing)
        {
            if (_forAll is { } answer)
                return answer;
            if (queue.ResolveConflict is null)
                return new(ConflictChoice.KeepBoth);
            answer = await queue.ResolveConflict(operation, new Conflict(item, existing, _itemsTotal - _itemsDone - 1));
            if (answer.ForAll)
                _forAll = answer;
            return answer;
        }

        private async Task<StorageItem?> Existing(IStorageProvider provider, StorageLocation folder, string name, CancellationToken? cancel = null)
        {
            await foreach (var child in provider.EnumerateAsync(folder, cancel ?? _cancel))
                if (string.Equals(child.Name, name, StringComparison.OrdinalIgnoreCase))
                    return child;
            return null;
        }

        private async Task<string> FreeName(IStorageProvider provider, StorageLocation folder, string name)
        {
            var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            await foreach (var child in provider.EnumerateAsync(folder, _cancel))
                taken.Add(child.Name);
            return StorageNames.Unique(name, taken.Contains);
        }

        private bool IsInside(StorageLocation target, StorageLocation folder)
        {
            for (StorageLocation? at = target; at is not null; at = queue._providers[at].GetParent(at))
                if (at.Equals(folder))
                    return true;
            return false;
        }

        private void Report(bool force = false)
        {
            var now = _clock.ElapsedMilliseconds;
            if (!force && now - _lastReport < 1000 / queue.ReportsPerSecond)
                return;
            _lastReport = now;
            var seconds = _clock.Elapsed.TotalSeconds;
            operation.Progress = new(_bytesDone, _bytesTotal, _itemsDone, _itemsTotal, _current, seconds > 0 ? _bytesDone / seconds : 0);
            queue.Changed?.Invoke(operation);
        }
    }
}
