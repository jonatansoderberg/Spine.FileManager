using System.Diagnostics;
using Spine.FileManager.Storage;

namespace Spine.FileManager.Operations;

/// <summary>
/// Runs copies and moves one at a time in the background, asks <see cref="ResolveConflict"/> when a name is taken,
/// and reports each operation through <see cref="Changed"/> at most <see cref="ReportsPerSecond"/> times a second.
/// </summary>
public sealed class OperationQueue(StorageProviders providers)
{
    // Up to this size a file is copied by the provider itself (APFS clones it); above it through a stream, so a large
    // file shows its bytes and can be cancelled midway. Spike 3 decides this properly (TransferPlanner).
    public const long NativeCopyLimit = 16 * 1024 * 1024;

    private const int BufferSize = 1024 * 1024;
    private readonly StorageProviders _providers = providers;
    private readonly SemaphoreSlim _running = new(1, 1);
    private readonly List<FileOperation> _operations = [];

    public int ReportsPerSecond { get; init; } = 10;

    /// <summary>Asked for every taken name, unless an earlier answer was "for all". Without it a conflict keeps both.</summary>
    public Func<FileOperation, Conflict, Task<ConflictAnswer>>? ResolveConflict { get; set; }

    /// <summary>Raised on a background thread for state changes and, throttled, for progress.</summary>
    public event Action<FileOperation>? Changed;

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
        var operation = new FileOperation(kind, [.. sources], target);
        lock (_operations)
            _operations.Add(operation);
        Changed?.Invoke(operation);
        _ = Task.Run(() => Run(operation));
        return operation;
    }

    /// <summary>Waits until <paramref name="operation"/> has finished, for tests and callers that need the result.</summary>
    public async Task WaitAsync(FileOperation operation)
    {
        while (!operation.IsFinished)
            await Task.Delay(10);
    }

    private async Task Run(FileOperation operation)
    {
        await _running.WaitAsync();
        var run = new Execution(this, operation);
        try
        {
            await run.Execute();
        }
        catch (OperationCanceledException)
        {
            operation.State = OperationState.Cancelled;
        }
        catch (Exception e)
        {
            operation.Errors.Add((operation.Target, e.Message));
            operation.State = OperationState.Failed;
        }
        finally
        {
            _running.Release();
            Changed?.Invoke(operation);
        }
    }

    /// <summary>The state of one operation while it runs.</summary>
    private sealed class Execution(OperationQueue queue, FileOperation operation)
    {
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private readonly CancellationToken _cancel = operation.Cancellation.Token;
        private long _bytesTotal, _bytesDone;
        private int _itemsTotal, _itemsDone;
        private string? _current;
        private long _lastReport;
        private ConflictAnswer? _forAll;
        // Items and bytes under each counted item, so a skipped or natively moved folder adds its share without being
        // listed again (after a move it is no longer there to list).
        private readonly Dictionary<StorageLocation, (int Items, long Bytes)> _subtree = [];

        public async Task Execute()
        {
            operation.State = OperationState.Preparing;
            queue.Changed?.Invoke(operation);
            var targetProvider = queue._providers[operation.Target];
            var sources = new List<StorageItem>();
            foreach (var source in operation.Sources)
            {
                var item = await queue._providers[source].GetItemAsync(source, _cancel);
                if (item.IsFolder && IsInside(operation.Target, source))
                    throw new StorageException(StorageErrorKind.NotSupported, source, $"“{item.Name}” cannot be copied or moved into itself.");
                sources.Add(item);
                await Count(item);
            }

            operation.State = OperationState.Running;
            Report(force: true);
            foreach (var item in sources)
            {
                _cancel.ThrowIfCancellationRequested();
                // Copied into its own folder, an item becomes its copy ("name (2)"), as Finder's Duplicate does.
                var sameFolder = queue._providers[item.Location].GetParent(item.Location)?.Equals(operation.Target) == true;
                if (sameFolder && operation.Kind == OperationKind.Move)
                {
                    operation.Results.Add(item.Location);
                    await Skip(item);
                    continue;
                }
                if (await Transfer(item, operation.Target, targetProvider, keepBoth: sameFolder) is { } result)
                    operation.Results.Add(result);
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
            _cancel.ThrowIfCancellationRequested();
            _current = item.Name;
            var source = queue._providers[item.Location];
            var name = item.Name;
            var existing = await Existing(target, folder, name);
            var replace = false;
            var merge = false;
            if (existing is not null)
            {
                var choice = keepBoth ? ConflictChoice.KeepBoth : (await Ask(item, existing)).Choice;
                switch (choice)
                {
                    case ConflictChoice.Stop:
                        operation.Cancel();
                        _cancel.ThrowIfCancellationRequested();
                        break;
                    case ConflictChoice.Skip:
                        await Skip(item);
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
                    await Skip(item);
                    return moved.Location;
                }

                StorageLocation result;
                if (item.IsFolder)
                {
                    var created = merge ? existing!.Location : (await target.CreateFolderAsync(folder, name, _cancel)).Location;
                    _itemsDone++;
                    Report();
                    var children = new List<StorageItem>();
                    await foreach (var child in source.EnumerateAsync(item.Location, _cancel))
                        children.Add(child);
                    foreach (var child in children)
                        await Transfer(child, created, target);
                    result = created;
                }
                else
                {
                    if (replace)
                        await target.DeleteAsync(existing!.Location, StorageDeleteMode.Permanent, _cancel);
                    result = sameProvider && capabilities.HasFlag(StorageCapabilities.NativeCopy) && (item.Size ?? 0) <= NativeCopyLimit
                        ? (await target.CopyAsync(item.Location, folder, name, _cancel)).Location
                        : await StreamCopy(item, source, target, folder, name);
                    _bytesDone += item.Size ?? 0;
                    _itemsDone++;
                    Report();
                }

                if (operation.Kind == OperationKind.Move)
                    await source.DeleteAsync(item.Location, StorageDeleteMode.Permanent, _cancel);
                return result;
            }
            catch (StorageException e)
            {
                operation.Errors.Add((item.Location, e.Message));
                await Skip(item);
                return null;
            }
        }

        private async Task<StorageLocation> StreamCopy(StorageItem item, IStorageProvider source, IStorageProvider target, StorageLocation folder, string name)
        {
            var start = _bytesDone;
            await using (var input = await source.OpenReadAsync(item.Location, _cancel))
            await using (var output = await target.OpenWriteAsync(folder, name, item.Size, overwrite: false, _cancel))
            {
                var buffer = new byte[BufferSize];
                int read;
                while ((read = await input.ReadAsync(buffer, _cancel)) > 0)
                {
                    await output.WriteAsync(buffer.AsMemory(0, read), _cancel);
                    _bytesDone += read;
                    Report();
                }
            }
            // The caller adds the whole file once it is done.
            _bytesDone = start;
            return (await Existing(target, folder, name))!.Location;
        }

        private Task Skip(StorageItem item)
        {
            var (items, bytes) = _subtree.TryGetValue(item.Location, out var counted) ? counted : (1, item.Size ?? 0);
            _itemsDone += items;
            _bytesDone += bytes;
            Report();
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

        private async Task<StorageItem?> Existing(IStorageProvider provider, StorageLocation folder, string name)
        {
            await foreach (var child in provider.EnumerateAsync(folder, _cancel))
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
