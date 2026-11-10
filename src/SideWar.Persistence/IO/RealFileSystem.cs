namespace SideWar.Persistence.IO;

public sealed class RealFileSystem : IFileSystem
{
    public static readonly RealFileSystem Instance = new();

    public bool FileExists(string path) => File.Exists(path);

    public void CreateDirectory(string path) => Directory.CreateDirectory(path);

    public void WriteAllBytesDurable(string path, ReadOnlySpan<byte> data)
    {
        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough);
        fs.Write(data);
        fs.Flush(flushToDisk: true);
    }

    public void Replace(string source, string destination, string backup) =>
        File.Replace(source, destination, backup, ignoreMetadataErrors: true);

    public void Move(string source, string destination, bool overwrite) => File.Move(source, destination, overwrite);

    public void Copy(string source, string destination, bool overwrite) => File.Copy(source, destination, overwrite);

    public void Delete(string path) => File.Delete(path);

    public long GetLength(string path) => new FileInfo(path).Length;

    public byte[] ReadAllBytes(string path) => File.ReadAllBytes(path);

    public IReadOnlyList<string> EnumerateFiles(string directory) =>
        Directory.Exists(directory) ? Directory.GetFiles(directory) : [];
}
