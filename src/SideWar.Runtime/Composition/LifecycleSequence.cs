namespace SideWar.Runtime.Composition;

/// <summary>
/// Ordem de inicialização e encerramento da raiz de composição (SDD-07 RF-07.10): os passos iniciam na ordem em que
/// foram adicionados e param na ordem inversa. Uma falha ao iniciar desfaz os já iniciados (reverso) e relança; uma
/// falha ao parar é isolada (os demais param) e devolvida. Cada passo para no máximo uma vez. Não é thread-safe:
/// cada sequência pertence a uma thread (T-UI ou T-Game).
/// </summary>
public sealed class LifecycleSequence
{
    private readonly List<Step> _steps = [];
    private readonly List<Step> _started = [];
    private readonly List<string> _journal = [];

    /// <summary>Registro "start:nome" / "stop:nome" / "fail:nome", na ordem (diagnóstico e testes).</summary>
    public IReadOnlyList<string> Journal => _journal;

    public int StartedCount => _started.Count;

    /// <summary>Passo com início e parada. Use <paramref name="start"/> nulo para recursos já criados (só parada).</summary>
    public LifecycleSequence Add(string name, Action? start, Action? stop)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        _steps.Add(new Step(name, start, stop));
        return this;
    }

    /// <summary>Registra e inicia agora um recurso já criado (parada no encerramento).</summary>
    public void Started(string name, Action stop)
    {
        var step = new Step(name, null, stop);
        _steps.Add(step);
        _started.Add(step);
        _journal.Add("start:" + name);
    }

    /// <summary>Inicia os passos ainda não iniciados, em ordem. Em falha: para os iniciados (reverso) e relança.</summary>
    public void StartAll()
    {
        foreach (var step in _steps)
        {
            if (_started.Contains(step)) continue;
            try
            {
                step.Start?.Invoke();
            }
            catch
            {
                _journal.Add("fail:" + step.Name);
                StopAll();
                throw;
            }
            _started.Add(step);
            _journal.Add("start:" + step.Name);
        }
    }

    /// <summary>Para os iniciados em ordem inversa; devolve as falhas (cada passo é isolado dos outros).</summary>
    public IReadOnlyList<Exception> StopAll()
    {
        var failures = new List<Exception>();
        for (int i = _started.Count - 1; i >= 0; i--)
        {
            var step = _started[i];
            _started.RemoveAt(i);
            try
            {
                step.Stop?.Invoke();
                _journal.Add("stop:" + step.Name);
            }
            catch (Exception e)
            {
                _journal.Add("fail:" + step.Name);
                failures.Add(e);
            }
        }
        return failures;
    }

    private sealed record Step(string Name, Action? Start, Action? Stop);
}
