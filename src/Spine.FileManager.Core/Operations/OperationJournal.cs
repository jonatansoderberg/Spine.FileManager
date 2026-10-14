using System.Text.Json;
using Spine.FileManager.Storage;

namespace Spine.FileManager.Operations;

/// <summary>
/// Every operation as one JSON file in a folder, written on each change of state and at most once a second while it
/// runs, so the app knows after a crash what was copying, how far it had come and which file was half written.
/// </summary>
public sealed class OperationJournal(string folder)
{
    /// <summary>Finished operations kept for the Operation Center's history.</summary>
    public const int History = 50;

    private static readonly JsonSerializerOptions Json = new();

    public void Save(FileOperation operation)
    {
        Directory.CreateDirectory(folder);
        Record record;
        lock (operation.Done)
            record = new(operation.Id, operation.Kind, [.. operation.Sources], operation.Target, operation.State, operation.Progress,
                [.. operation.Errors], [.. operation.Results], [.. operation.Done], [.. operation.CreatedFolders], operation.Writing,
                operation.Created, operation.Updated, operation.Attempts);
        var path = Path.Combine(folder, $"{operation.Id}.json");
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(record, Json));
        File.Move(temporary, path, overwrite: true);
    }

    public void Delete(Guid id) => File.Delete(Path.Combine(folder, $"{id}.json"));

    /// <summary>The journaled operations, oldest first; damaged files are skipped and kept aside.</summary>
    public IReadOnlyList<FileOperation> Load()
    {
        if (!Directory.Exists(folder))
            return [];
        var operations = new List<FileOperation>();
        foreach (var path in Directory.GetFiles(folder, "*.json"))
        {
            try
            {
                if (JsonSerializer.Deserialize<Record>(File.ReadAllText(path), Json) is { } record)
                    operations.Add(record.ToOperation());
            }
            catch (JsonException)
            {
                File.Move(path, path + ".broken", overwrite: true);
            }
        }
        operations.Sort((a, b) => a.Created.CompareTo(b.Created));
        // History beyond the limit is dropped, oldest first; unfinished operations always stay.
        foreach (var old in operations.Where(o => o.IsFinished).SkipLast(History).ToList())
        {
            Delete(old.Id);
            operations.Remove(old);
        }
        return operations;
    }

    private sealed record Record(
        Guid Id, OperationKind Kind, List<StorageLocation> Sources, StorageLocation Target, OperationState State, OperationProgress Progress,
        List<OperationError> Errors, List<StorageLocation> Results, List<string> Done, List<string> CreatedFolders, StorageLocation? Writing,
        DateTimeOffset Created, DateTimeOffset Updated, int Attempts)
    {
        public FileOperation ToOperation()
        {
            var operation = new FileOperation(Id, Kind, Sources, Target, Created)
            {
                Progress = Progress,
                Updated = Updated,
                Attempts = Attempts,
                Writing = Writing,
                // Whatever was still going when the journal was written stopped with the app.
                State = State is OperationState.Queued or OperationState.Preparing or OperationState.Running or OperationState.Paused
                    ? OperationState.Interrupted
                    : State,
            };
            operation.Errors.AddRange(Errors);
            operation.Results.AddRange(Results);
            operation.Done.UnionWith(Done);
            operation.CreatedFolders.UnionWith(CreatedFolders);
            return operation;
        }
    }
}
