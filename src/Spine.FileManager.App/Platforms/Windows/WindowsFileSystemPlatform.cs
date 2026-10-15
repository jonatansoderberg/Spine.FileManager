using System.Runtime.InteropServices;
using Spine.FileManager.Storage;
using Spine.FileManager.Storage.FileSystem;

namespace Spine.FileManager.Storage;

/// <summary>Explorer's places, drives, OneDrive and the Recycle Bin on Windows.</summary>
public sealed class WindowsFileSystemPlatform : DefaultFileSystemPlatform
{
    static readonly Guid Downloads = new("374DE290-123F-4565-9164-39C4925E467B");

    public override IReadOnlyList<(string Path, string Name, StorageRootKind Kind)> GetRoots()
    {
        var roots = new List<(string, string, StorageRootKind)>();
        void Add(string? path, StorageRootKind kind, string? name = null)
        {
            if (!string.IsNullOrEmpty(path) && Directory.Exists(path) && !roots.Any(r => string.Equals(r.Item1, path, StringComparison.OrdinalIgnoreCase)))
                roots.Add((path, name ?? Path.GetFileName(path.TrimEnd('\\')), kind));
        }

        Add(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), StorageRootKind.Home);
        Add(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), StorageRootKind.Desktop);
        Add(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), StorageRootKind.Documents);
        Add(KnownFolder(Downloads), StorageRootKind.Downloads);
        Add(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), StorageRootKind.Pictures);
        Add(Environment.GetFolderPath(Environment.SpecialFolder.MyMusic), StorageRootKind.Music);
        Add(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), StorageRootKind.Videos);
        foreach (var variable in new[] { "OneDriveConsumer", "OneDriveCommercial", "OneDrive" })
            Add(Environment.GetEnvironmentVariable(variable), StorageRootKind.Cloud);
        foreach (var drive in DriveInfo.GetDrives())
        {
            if (!drive.IsReady || drive.DriveType is DriveType.CDRom or DriveType.NoRootDirectory or DriveType.Unknown)
                continue;
            var label = string.IsNullOrEmpty(drive.VolumeLabel) ? drive.Name.TrimEnd('\\') : $"{drive.VolumeLabel} ({drive.Name.TrimEnd('\\')})";
            Add(drive.RootDirectory.FullName, drive.DriveType switch
            {
                DriveType.Removable => StorageRootKind.RemovableVolume,
                DriveType.Network => StorageRootKind.NetworkVolume,
                _ => StorageRootKind.Volume,
            }, label);
        }
        return roots;
    }

    public override bool CanWatch => true;

    // The Recycle Bin exists on fixed drives only; SHFileOperation with FOF_ALLOWUNDO silently deletes for good elsewhere,
    // so other drives report no trash and the app asks before a permanent delete.
    public override bool CanTrash(string path)
    {
        try
        {
            return Path.GetPathRoot(path) is { } root && !root.StartsWith(@"\\", StringComparison.Ordinal) && new DriveInfo(root).DriveType == DriveType.Fixed;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    public override Task MoveToTrashAsync(string path, CancellationToken cancellationToken)
    {
        var operation = new SHFILEOPSTRUCT
        {
            wFunc = FO_DELETE,
            pFrom = path + "\0\0",
            fFlags = FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_NOERRORUI | FOF_SILENT,
        };
        var result = SHFileOperation(ref operation);
        if (result != 0 || operation.fAnyOperationsAborted)
            throw new IOException($"Could not move {path} to the Recycle Bin (error {result}).");
        return Task.CompletedTask;
    }

    // The Recycle Bin of every drive together, as Explorer counts it; it is no folder the explorer can list.
    public override Task<TrashContents?> MeasureTrashAsync(CancellationToken cancellationToken)
    {
        var info = new SHQUERYRBINFO { cbSize = Marshal.SizeOf<SHQUERYRBINFO>() };
        return Task.FromResult(SHQueryRecycleBin(null, ref info) == 0 ? new TrashContents((int)info.i64NumItems, info.i64Size) : null);
    }

    // The app has asked already; the shell neither asks again nor shows its own progress.
    public override Task<IReadOnlyList<string>> EmptyTrashAsync(IProgress<double>? progress, CancellationToken cancellationToken) => Task.Run(() =>
    {
        var result = SHEmptyRecycleBin(0, null, SHERB_NOCONFIRMATION | SHERB_NOPROGRESSUI | SHERB_NOSOUND);
        // S_OK, or E_UNEXPECTED when it was empty already.
        if (result != 0 && result != unchecked((int)0x8000FFFF))
            throw new IOException($"Could not empty the Recycle Bin (error 0x{result:X8}).");
        progress?.Report(1);
        return (IReadOnlyList<string>)[];
    }, cancellationToken);

    public override void ShowTrash() =>
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", "shell:RecycleBinFolder") { UseShellExecute = true });

    // A drive root shows its label, as Explorer does: "Data (D:)".
    public override string? FolderDisplayName(string path, string name)
    {
        if (Path.GetPathRoot(path) != path)
            return null;
        try
        {
            var drive = new DriveInfo(path);
            return drive.IsReady && drive.VolumeLabel.Length > 0 ? $"{drive.VolumeLabel} ({path.TrimEnd('\\')})" : path.TrimEnd('\\');
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    static string? KnownFolder(Guid id)
    {
        if (SHGetKnownFolderPath(id, 0, 0, out var path) != 0)
            return null;
        try
        {
            return Marshal.PtrToStringUni(path);
        }
        finally
        {
            Marshal.FreeCoTaskMem(path);
        }
    }

    const uint FO_DELETE = 3;
    const ushort FOF_SILENT = 0x0004, FOF_NOCONFIRMATION = 0x0010, FOF_ALLOWUNDO = 0x0040, FOF_NOERRORUI = 0x0400;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct SHFILEOPSTRUCT
    {
        public nint hwnd;
        public uint wFunc;
        public string pFrom;
        public string? pTo;
        public ushort fFlags;
        [MarshalAs(UnmanagedType.Bool)] public bool fAnyOperationsAborted;
        public nint hNameMappings;
        public string? lpszProgressTitle;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    static extern int SHFileOperation(ref SHFILEOPSTRUCT operation);

    const uint SHERB_NOCONFIRMATION = 0x1, SHERB_NOPROGRESSUI = 0x2, SHERB_NOSOUND = 0x4;

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    struct SHQUERYRBINFO
    {
        public int cbSize;
        public long i64Size;
        public long i64NumItems;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    static extern int SHQueryRecycleBin(string? rootPath, ref SHQUERYRBINFO info);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    static extern int SHEmptyRecycleBin(nint window, string? rootPath, uint flags);

    [DllImport("shell32.dll")]
    static extern int SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid id, uint flags, nint token, out nint path);
}
