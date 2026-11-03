using FramePilot.Core.Models;

namespace FramePilot.GameAdapters;

/// <summary>
/// Decides when to start an automatic recording (spec 2026-11-04 §3.5): once per game launch (process id),
/// only after the game has been running for the configured delay. Pure logic: the process probe is injected.
/// </summary>
public sealed class AutoCaptureWatcher
{
    private readonly Func<GameId, int?> _probe;
    private readonly Dictionary<GameId, (int Pid, DateTimeOffset Since, bool Fired)> _seen = new();

    /// <param name="probe">Returns the process id of the running game, or null.</param>
    public AutoCaptureWatcher(Func<GameId, int?> probe) => _probe = probe;

    /// <summary>Call every couple of seconds. Returns the game to record now, or null.</summary>
    public GameId? Poll(DateTimeOffset now, TimeSpan delay, bool enabled, bool busy)
    {
        GameId? due = null;
        foreach (var game in new[] { GameId.Cs2, GameId.Valorant })
        {
            var pid = _probe(game);
            if (pid is null) { _seen.Remove(game); continue; }
            if (!_seen.TryGetValue(game, out var s) || s.Pid != pid)
                _seen[game] = s = (pid.Value, now, false);
            if (!enabled || busy || s.Fired || due is not null) continue;
            if (now - s.Since >= delay)
            {
                _seen[game] = s with { Fired = true };
                due = game;
            }
        }
        return due;
    }
}
