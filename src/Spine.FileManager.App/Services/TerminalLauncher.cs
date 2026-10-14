using System.Runtime.InteropServices;

namespace Spine.FileManager.Services;

/// <summary>A terminal the user has, which a folder can be opened in.</summary>
public sealed record Terminal(string Name, string Id);

/// <summary>
/// Opens a folder in a terminal: Terminal (and iTerm, Warp or Ghostty when installed) on the Mac; Windows Terminal,
/// PowerShell or Command Prompt on Windows. Launching processes stays in this service so a sandboxed build can leave
/// it out (docs/plan.md, Decisions).
/// </summary>
public static class TerminalLauncher
{
    public static bool IsSupported => OperatingSystem.IsMacCatalyst() || OperatingSystem.IsWindows();

    public static IReadOnlyList<Terminal> Installed { get; } = Find();

    private static IReadOnlyList<Terminal> Find()
    {
#if MACCATALYST
        var terminals = new List<Terminal> { new("Terminal", "Terminal") };
        foreach (var app in new[] { "iTerm", "Warp", "Ghostty" })
            if (Directory.Exists($"/Applications/{app}.app"))
                terminals.Add(new(app, app));
        return terminals;
#elif WINDOWS
        var terminals = new List<Terminal>();
        if (OnPath("wt.exe") is not null)
            terminals.Add(new("Windows Terminal", "wt"));
        terminals.Add(new("PowerShell", OnPath("pwsh.exe") is not null ? "pwsh" : "powershell"));
        terminals.Add(new("Command Prompt", "cmd"));
        return terminals;
#else
        return [];
#endif
    }

    /// <summary>Opens <paramref name="folder"/> in <paramref name="terminal"/> (the first installed when null); false when it could not start.</summary>
    public static bool Open(string folder, Terminal? terminal = null)
    {
        terminal ??= Installed.FirstOrDefault();
        if (terminal is null || !Directory.Exists(folder))
            return false;
#if MACCATALYST
        return Spawn("/usr/bin/open", ["-a", terminal.Id, folder]);
#elif WINDOWS
        var start = terminal.Id switch
        {
            "wt" => new System.Diagnostics.ProcessStartInfo("wt.exe") { ArgumentList = { "-d", folder } },
            "cmd" => new System.Diagnostics.ProcessStartInfo("cmd.exe") { ArgumentList = { "/K", "cd", "/d", folder } },
            var shell => new System.Diagnostics.ProcessStartInfo($"{shell}.exe") { ArgumentList = { "-NoExit" } },
        };
        start.WorkingDirectory = folder;
        start.UseShellExecute = false;
        try
        {
            return System.Diagnostics.Process.Start(start) is not null;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
#else
        return false;
#endif
    }

#if WINDOWS
    private static string? OnPath(string exe) =>
        (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator).Select(d => Path.Combine(d, exe)).FirstOrDefault(File.Exists)
        ?? (exe == "wt.exe" && Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) is { } local && File.Exists(Path.Combine(local, "Microsoft", "WindowsApps", exe)) ? exe : null);
#endif

#if MACCATALYST
    // Process.Start is not available on Mac Catalyst; posix_spawn is, outside the App Sandbox.
    internal static bool Spawn(string path, string[] arguments)
    {
        var argv = new[] { path }.Concat(arguments).Select(Marshal.StringToHGlobalAnsi).Append(0).ToArray();
        try
        {
            return posix_spawn(out var pid, path, 0, 0, argv, Environ()) == 0 && pid > 0;
        }
        finally
        {
            foreach (var arg in argv.Where(a => a != 0))
                Marshal.FreeHGlobal(arg);
        }
    }

    private static nint Environ() => Marshal.ReadIntPtr(_NSGetEnviron());

    [DllImport("libc")]
    private static extern int posix_spawn(out int pid, string path, nint fileActions, nint attributes, nint[] argv, nint envp);

    [DllImport("libc")]
    private static extern nint _NSGetEnviron();
#endif
}
