namespace Spine.FileManager.Tests.Storage;

/// <summary>A scratch folder under the temp directory, removed again when the test ends.</summary>
public sealed class TempFolder : IDisposable
{
    public string Path { get; } = Directory.CreateTempSubdirectory("spine-fm-").FullName;

    public string File(string name, string content = "x")
    {
        var path = System.IO.Path.Combine(Path, name);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        System.IO.File.WriteAllText(path, content);
        return path;
    }

    public string Folder(string name) => Directory.CreateDirectory(System.IO.Path.Combine(Path, name)).FullName;

    public void Dispose()
    {
        try { Directory.Delete(Path, recursive: true); } catch { }
    }
}
