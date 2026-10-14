using System.Collections.ObjectModel;
using Plugin.Maui.Spine.Common;
using Spine.FileManager.Explorer;
using Spine.FileManager.Operations;

namespace Spine.FileManager.Pages;

/// <summary>One operation in the Operation Center, with the texts and actions its state allows.</summary>
public sealed partial class OperationRow(FileOperation operation) : ObservableObject
{
    public FileOperation Operation { get; } = operation;

    [ObservableProperty] public partial string Title { get; private set; } = "";
    [ObservableProperty] public partial string Detail { get; private set; } = "";
    [ObservableProperty] public partial double Progress { get; private set; }
    [ObservableProperty] public partial bool ShowsProgress { get; private set; }
    [ObservableProperty] public partial bool CanPause { get; private set; }
    [ObservableProperty] public partial bool CanContinue { get; private set; }
    [ObservableProperty] public partial bool CanCancel { get; private set; }
    [ObservableProperty] public partial bool CanResume { get; private set; }
    [ObservableProperty] public partial bool CanReorder { get; private set; }
    [ObservableProperty] public partial bool CanRemove { get; private set; }
    [ObservableProperty] public partial string ResumeText { get; private set; } = "";

    public void Refresh(ISpineStrings strings)
    {
        var operation = Operation;
        var progress = operation.Progress;
        var count = operation.Sources.Count == 1 ? $"“{Path.GetFileName(operation.Sources[0].DisplayPath)}”" : strings.Get("Explorer.Items", operation.Sources.Count);
        var target = Path.GetFileName(operation.Target.DisplayPath.TrimEnd(Path.DirectorySeparatorChar));
        Title = string.Format(strings[operation.Kind == OperationKind.Move ? "Operations.MoveTitle" : "Operations.CopyTitle"], count, target);
        var amount = progress.BytesTotal > 0 ? $"{ByteSize.Format(progress.BytesDone)} / {ByteSize.Format(progress.BytesTotal)}" : "";
        var speed = operation.State == OperationState.Running && progress.BytesPerSecond > 1_000_000 ? $" · {ByteSize.Format((long)progress.BytesPerSecond)}/s" : "";
        var errors = operation.Errors.Count > 0 ? $" · {strings.Get("Operations.ErrorCount", operation.Errors.Count)}" : "";
        Detail = $"{strings[$"Operations.State.{operation.State}"]}{(amount.Length > 0 ? " · " + amount : "")}{speed}{errors} · {operation.Updated.LocalDateTime:g}";
        Progress = progress.Fraction;
        ShowsProgress = !operation.IsFinished;
        CanPause = operation.State == OperationState.Running;
        CanContinue = operation.State == OperationState.Paused;
        CanCancel = operation.State is OperationState.Running or OperationState.Paused or OperationState.Queued or OperationState.Preparing;
        CanResume = operation.CanResume;
        ResumeText = strings[operation.State == OperationState.Interrupted ? "Operations.Resume" : "Operations.Retry"];
        CanReorder = operation.State == OperationState.Queued;
        CanRemove = operation.IsFinished || operation.State == OperationState.Interrupted;
    }
}

/// <summary>
/// The full Operation Center (plan §12): every copy and move the queue knows — running, queued, interrupted by a
/// quit or crash, and the recent history — with pause, continue, cancel, resume or retry, reorder and show.
/// </summary>
public partial class OperationsPageViewModel : ViewModelBase
{
    private readonly ISpineStrings _strings;
    private readonly OperationQueue _queue;
    private readonly INavigationService _navigation;

    public OperationsPageViewModel(ISpineStrings strings, OperationQueue queue, INavigationService navigation)
    {
        _strings = strings;
        _queue = queue;
        _navigation = navigation;
        _queue.Changed += _ => MainThread.BeginInvokeOnMainThread(Refresh);
    }

    public ObservableCollection<OperationRow> Rows { get; } = [];

    public bool IsEmpty => Rows.Count == 0;

    public override Task OnAppearingAsync(NavigationDirection navigationDirection)
    {
        Title = _strings["Operations.Title"];
        Refresh();
        return Task.CompletedTask;
    }

    // Newest first: what runs and waits on top, then what stopped, then the history.
    private void Refresh()
    {
        var operations = _queue.Operations.Reverse().OrderBy(o => o.State switch
        {
            OperationState.Running or OperationState.Paused or OperationState.Preparing => 0,
            OperationState.Queued => 1,
            OperationState.Interrupted or OperationState.Failed => 2,
            _ => 3,
        }).ToList();
        for (var i = Rows.Count - 1; i >= 0; i--)
            if (!operations.Contains(Rows[i].Operation))
                Rows.RemoveAt(i);
        for (var i = 0; i < operations.Count; i++)
        {
            var row = Rows.FirstOrDefault(r => r.Operation == operations[i]);
            if (row is null)
                Rows.Insert(i, row = new OperationRow(operations[i]));
            else if (Rows.IndexOf(row) != i)
                Rows.Move(Rows.IndexOf(row), i);
            row.Refresh(_strings);
        }
        OnPropertyChanged(nameof(IsEmpty));
    }

    [RelayCommand] private void Pause(OperationRow row) => _queue.Pause(row.Operation);
    [RelayCommand] private void Continue(OperationRow row) => _queue.Continue(row.Operation);
    [RelayCommand] private void Cancel(OperationRow row) => row.Operation.Cancel();
    [RelayCommand] private void Resume(OperationRow row) => _queue.Resume(row.Operation);
    [RelayCommand] private void MoveUp(OperationRow row) => _queue.Move(row.Operation, -1);
    [RelayCommand] private void MoveDown(OperationRow row) => _queue.Move(row.Operation, 1);
    [RelayCommand] private void Remove(OperationRow row) => _queue.Remove(row.Operation);

    [RelayCommand]
    private async Task Show(OperationRow row)
    {
        await _navigation.CloseAsync();
        if (Services.AppMenu.Explorer is { } explorer)
            await explorer.OpenLocationCommand.ExecuteAsync(row.Operation.Target);
    }

    [PageAction(Svg = "Clean.svg")]
    [RelayCommand]
    private void ClearFinished()
    {
        foreach (var operation in _queue.Operations.Where(o => o.State == OperationState.Completed))
            _queue.Remove(operation);
        Refresh();
    }
}
