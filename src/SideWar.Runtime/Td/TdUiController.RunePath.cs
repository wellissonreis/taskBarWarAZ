using SideWar.Core.Td;

namespace SideWar.Runtime.Td;

public sealed partial class TdUiController
{
    private const double RunePathInterval = 0.3;
    private string? RunePathNotice => _now() < _runePathNoticeUntil ? _runePathNotice : null;

    private void StartRunePath(TdMatchState match, string type)
    {
        _runePath.Clear(); _runePathPending = null; _runePathNotice = null;
        if (match.Phase is not (MatchPhase.Preparo or MatchPhase.Onda)) return;
        foreach (var id in TdRunePath.Find(match.RuneWeb, match.Frontier, match.IsAwakened, match.RuneCost, type)) _runePath.Enqueue(id);
        _runePathNext = _now();
        SyncRunePath(match);
        Changed?.Invoke();
    }

    private void SyncRunePath(TdMatchState match)
    {
        if (_runePathPending is not null)
        {
            if (!match.IsAwakened(_runePathPending)) return;
            _runePathPending = null;
            _runePathNext = _now() + RunePathInterval;
        }
        if (_runePath.Count == 0 || _now() < _runePathNext) return;
        if (match.Phase is not (MatchPhase.Preparo or MatchPhase.Onda)) { _runePath.Clear(); return; }
        var id = _runePath.Peek();
        if (match.IsAwakened(id)) { _runePath.Dequeue(); return; }
        if (!match.Frontier.Contains(id)) { _runePath.Clear(); return; }
        if (match.Fragments < match.RuneCost(id))
        {
            _runePath.Clear();
            _runePathNotice = "Faltam fragmentos para continuar.";
            _runePathNoticeUntil = _now() + StageNoticeSeconds;
            Changed?.Invoke(); return;
        }
        _runePath.Dequeue();
        _runePathPending = id;
        _send(new AwakenRune(id));
        Changed?.Invoke();
    }
}
