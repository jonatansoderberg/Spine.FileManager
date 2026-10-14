using System.Diagnostics;
using System.Text;

namespace FileSystemSpike;

public enum Outcome { Ok, Denied, Failed, Unsupported, Skipped }

public sealed record StepResult(string Step, Outcome Outcome, double Milliseconds, string? Detail);

public sealed class SkipException(string reason) : Exception(reason);

/// <summary>The steps run against one location and what each gave.</summary>
public sealed class LocationReport(string name, string target, string via)
{
    public string Name { get; } = name;
    public string Target { get; } = target;
    public string Via { get; } = via;
    public List<StepResult> Steps { get; } = [];
    public List<string> Notes { get; } = [];

    public Outcome this[string step] => Steps.LastOrDefault(s => s.Step == step)?.Outcome ?? Outcome.Skipped;

    public bool Run(string step, Func<string?> action)
    {
        var watch = Stopwatch.StartNew();
        try
        {
            var detail = action();
            Steps.Add(new(step, Outcome.Ok, watch.Elapsed.TotalMilliseconds, detail));
            return true;
        }
        catch (Exception e)
        {
            Steps.Add(new(step, Classify(e), watch.Elapsed.TotalMilliseconds, Describe(e)));
            return false;
        }
    }

    public async Task<bool> RunAsync(string step, Func<Task<string?>> action)
    {
        var watch = Stopwatch.StartNew();
        try
        {
            var detail = await action();
            Steps.Add(new(step, Outcome.Ok, watch.Elapsed.TotalMilliseconds, detail));
            return true;
        }
        catch (Exception e)
        {
            Steps.Add(new(step, Classify(e), watch.Elapsed.TotalMilliseconds, Describe(e)));
            return false;
        }
    }

    static Outcome Classify(Exception e) => e switch
    {
        SkipException => Outcome.Skipped,
        UnauthorizedAccessException => Outcome.Denied,
        PlatformNotSupportedException or NotSupportedException => Outcome.Unsupported,
        _ when e.GetType().Name.Contains("Security", StringComparison.Ordinal) => Outcome.Denied,
        _ when e.GetType().Name.Contains("UnsupportedOperation", StringComparison.Ordinal) => Outcome.Unsupported,
        _ => Outcome.Failed,
    };

    static string Describe(Exception e) => e is SkipException ? e.Message : $"{e.GetType().Name}: {e.Message.ReplaceLineEndings(" ")}";
}

public static class ReportWriter
{
    static readonly string[] Columns = ["list", "read", "create-folder", "write", "random-access", "rename", "copy", "move", "watch", "delete"];

    static string Mark(Outcome outcome) => outcome switch
    {
        Outcome.Ok => "✅",
        Outcome.Denied => "⛔",
        Outcome.Failed => "❌",
        Outcome.Unsupported => "—",
        _ => "·",
    };

    public static string Render(string title, IReadOnlyList<LocationReport> reports)
    {
        var text = new StringBuilder();
        text.AppendLine($"# {title}").AppendLine();
        text.AppendLine($"{DeviceInfo.Platform} {DeviceInfo.VersionString} · {DeviceInfo.Model} · {DeviceInfo.DeviceType} · {DateTime.Now:yyyy-MM-dd HH:mm}");
        foreach (var line in ProcessContext.Lines())
            text.AppendLine($"- {line}");
        text.AppendLine();
        text.AppendLine("✅ ok · ⛔ denied · ❌ failed · — unsupported · · not run").AppendLine();
        text.AppendLine($"| Location | Via | {string.Join(" | ", Columns)} |");
        text.AppendLine($"|---|---|{string.Concat(Columns.Select(_ => "---|"))}");
        foreach (var report in reports)
        {
            var exists = report["exists"];
            var cells = exists == Outcome.Ok ? Columns.Select(c => Mark(report[c])) : Columns.Select(_ => " ");
            var name = exists == Outcome.Ok ? report.Name : $"{report.Name} ({(exists == Outcome.Denied ? "denied" : "missing")})";
            text.AppendLine($"| {name} | {report.Via} | {string.Join(" | ", cells)} |");
        }
        text.AppendLine();
        foreach (var report in reports)
        {
            text.AppendLine($"## {report.Name}").AppendLine();
            text.AppendLine($"`{report.Target}` via {report.Via}").AppendLine();
            foreach (var note in report.Notes)
                text.AppendLine($"- {note}");
            if (report.Notes.Count > 0)
                text.AppendLine();
            text.AppendLine("| Step | Result | ms | Detail |").AppendLine("|---|---|---:|---|");
            foreach (var step in report.Steps)
                text.AppendLine($"| {step.Step} | {Mark(step.Outcome)} {step.Outcome} | {step.Milliseconds:0.#} | {step.Detail?.Replace("|", "\\|")} |");
            text.AppendLine();
        }
        return text.ToString();
    }
}
