using System.Net.Sockets;
using Spine.FileManager.DeviceLink;

namespace Spine.FileManager.Services;

/// <summary>
/// This installation among Other Devices (plan §19–21): its node identity, the devices it trusts and the node that
/// listens for them. The node starts when it is first needed, so a Mac that links nothing never opens a port.
/// </summary>
public sealed class DeviceService
{
    /// <summary>Where the node listens, when the port is free; a fixed port lets a device find it again after a restart.</summary>
    public const int Port = 47821;

    private const string KeyName = "node-key";
    private readonly SemaphoreSlim _starting = new(1, 1);
    private LinkNode? _node;

    public TrustStore Trust { get; } = new(Path.Combine(AppPaths.Data, "trust.json"));

    /// <summary>A file arrived: the sending device's name and the file's path.</summary>
    public event Action<string, string>? Received;

    /// <summary>A device paired through this one's invitation.</summary>
    public event Action<TrustedNode>? Paired;

    /// <summary>Where received files go: Downloads on a computer, as with AirDrop; on a phone the app's documents, which the Files app shows.</summary>
    public static string Inbox => DeviceInfo.Idiom == DeviceIdiom.Desktop
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Received");

    /// <summary>The name other devices see: the computer's name on a Mac, which Mac Catalyst reports as "iPad".</summary>
    public static string DeviceName =>
#if MACCATALYST
        ObjCRuntime.Runtime.GetNSObject<Foundation.NSString>(ComputerName(IntPtr.Zero, IntPtr.Zero), owns: true)?.ToString() ?? DeviceInfo.Name;

    [System.Runtime.InteropServices.DllImport("/System/Library/Frameworks/SystemConfiguration.framework/SystemConfiguration", EntryPoint = "SCDynamicStoreCopyComputerName")]
    private static extern IntPtr ComputerName(IntPtr store, IntPtr encoding);
#else
        DeviceInfo.Name;
#endif

    public async Task<LinkNode> GetNodeAsync()
    {
        if (_node is not null)
            return _node;
        await _starting.WaitAsync();
        try
        {
            if (_node is null)
            {
                var node = new LinkNode(await LoadIdentityAsync(), Trust, DeviceName, Inbox);
                try
                {
                    node.Start(Port);
                }
                catch (SocketException)
                {
                    node.Start();
                }
                node.Received += (from, path) => Received?.Invoke(Trust.Find(from)?.DisplayName ?? from, path);
                node.Paired += trusted => Paired?.Invoke(trusted);
                _node = node;
            }
            return _node;
        }
        finally
        {
            _starting.Release();
        }
    }

    /// <summary>Starts listening at launch when some device is already linked, so it can send files here.</summary>
    public void StartIfLinked()
    {
        if (Trust.All.Count > 0)
            _ = GetNodeAsync();
    }

    public async Task SendAsync(string nodeId, IReadOnlyList<string> paths, Action<long, long> progress) =>
        await (await GetNodeAsync()).SendAsync(nodeId, paths, progress);

    // The private key goes to the platform's secure storage (Keychain, Keystore, DPAPI). Where that is not available
    // to the app (an unsigned Mac build has no keychain access group), a file only the user can read keeps it.
    private static async Task<NodeIdentity> LoadIdentityAsync()
    {
        var file = Path.Combine(AppPaths.Data, "node.key");
        try
        {
            if (await SecureStorage.Default.GetAsync(KeyName) is { } stored)
                return NodeIdentity.Import(Convert.FromBase64String(stored));
            var created = NodeIdentity.Create();
            await SecureStorage.Default.SetAsync(KeyName, Convert.ToBase64String(created.ExportPrivateKey()));
            return created;
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            if (File.Exists(file))
                return NodeIdentity.Import(Convert.FromBase64String(await File.ReadAllTextAsync(file)));
            var created = NodeIdentity.Create();
            Directory.CreateDirectory(AppPaths.Data);
            await File.WriteAllTextAsync(file, Convert.ToBase64String(created.ExportPrivateKey()));
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            return created;
        }
    }
}
