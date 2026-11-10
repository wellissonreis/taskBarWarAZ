using SideWar.Persistence.IO;

namespace SideWar.Persistence.Tests;

/// <summary>
/// Arquivos em memória indexados por caminho normalizado ('\\' → '/'). O código de produção usa
/// Path.Combine, que no Windows gera '\\'; os testes escrevem caminhos com '/'. Normalizar aqui
/// mantém as asserções idênticas nos dois SOs sem alterar o que elas verificam.
/// </summary>
public sealed class FakeFiles
{
    private readonly Dictionary<string, byte[]> _d = new(StringComparer.Ordinal);
    public static string Norm(string path) => path.Replace('\\', '/');
    public byte[] this[string path] { get => _d[Norm(path)]; set => _d[Norm(path)] = value; }
    public bool ContainsKey(string path) => _d.ContainsKey(Norm(path));
    public bool Remove(string path) => _d.Remove(Norm(path));
    public IReadOnlyCollection<string> Keys => _d.Keys;
}

/// <summary>FS em memória com injeção de falhas por operação.</summary>
public sealed class FakeFileSystem : IFileSystem
{
    public FakeFiles Files { get; } = new();
    public List<string> Calls { get; } = [];
    /// <summary>Se definido, é chamado antes de cada operação (nome, caminho); pode lançar.</summary>
    public Action<string, string>? Fault { get; set; }

    private void Op(string op, string path) { path = FakeFiles.Norm(path); Calls.Add(op + ":" + path); Fault?.Invoke(op, path); }

    public bool FileExists(string path) => Files.ContainsKey(path);
    public void CreateDirectory(string path) { }
    public void WriteAllBytesDurable(string path, ReadOnlySpan<byte> data) { Op("write", path); Files[path] = data.ToArray(); }
    public void Replace(string source, string destination, string backup)
    {
        Op("replace", destination);
        Files[backup] = Files[destination];
        Files[destination] = Files[source];
        Files.Remove(source);
    }
    public void Move(string source, string destination, bool overwrite)
    {
        Op("move", destination);
        if (!overwrite && Files.ContainsKey(destination)) throw new IOException("exists");
        Files[destination] = Files[source];
        Files.Remove(source);
    }
    public void Copy(string source, string destination, bool overwrite) { Op("copy", destination); Files[destination] = Files[source]; }
    public void Delete(string path) { Op("delete", path); Files.Remove(path); }
    public long GetLength(string path) => Files[path].Length;
    public byte[] ReadAllBytes(string path) => Files[path];
    public IReadOnlyList<string> EnumerateFiles(string directory)
    {
        var dir = FakeFiles.Norm(directory).TrimEnd('/');
        return Files.Keys.Where(k => k.Contains('/') && k[..k.LastIndexOf('/')] == dir).ToList();
    }
}
