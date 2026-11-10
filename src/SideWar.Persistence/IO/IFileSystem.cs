namespace SideWar.Persistence.IO;

/// <summary>Porta fina de sistema de arquivos (SDD-14 §7.1), com implementação real e falsa nos testes.</summary>
public interface IFileSystem
{
    bool FileExists(string path);
    void CreateDirectory(string path);
    /// <summary>FileMode.Create, FileShare.None, WriteThrough e Flush(flushToDisk:true).</summary>
    void WriteAllBytesDurable(string path, ReadOnlySpan<byte> data);
    void Replace(string source, string destination, string backup);
    void Move(string source, string destination, bool overwrite);
    void Copy(string source, string destination, bool overwrite);
    void Delete(string path);
    long GetLength(string path);
    byte[] ReadAllBytes(string path);
    IReadOnlyList<string> EnumerateFiles(string directory);
}
