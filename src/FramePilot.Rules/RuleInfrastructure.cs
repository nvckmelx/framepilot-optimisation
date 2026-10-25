using FramePilot.Core.Models;

namespace FramePilot.Rules;

public sealed record RuleContext(SystemSnapshot Snapshot, GameId? TargetGame, SessionRecord? LatestSession, PerformanceGoal Goal = PerformanceGoal.StableFps);

public interface IRule
{
    RuleMetadata Meta { get; }
    Recommendation? Evaluate(RuleContext context);
}

public static class Sources
{
    public static readonly SourceRef NvidiaReflex = new("NVIDIA Reflex Low Latency Platform", "https://www.nvidia.com/en-us/geforce/news/reflex-low-latency-platform/");
    public static readonly SourceRef NvidiaLatencyGuide = new("NVIDIA: How To Reduce Lag — A Guide To Better System Latency", "https://www.nvidia.com/en-us/geforce/guides/system-latency-optimization-guide/");
    public static readonly SourceRef PresentMon = new("Intel GameTechDev PresentMon", "https://github.com/GameTechDev/PresentMon");
    public static readonly SourceRef WindowedOptimizations = new("Microsoft Support: Optimizations for windowed games in Windows 11", "https://support.microsoft.com/en-us/topic/3f006843-2c7e-4ed0-9a5e-f9389e535952");
    public static readonly SourceRef WindowsGraphicsSettings = new("Microsoft Support: Windows graphics settings", "https://support.microsoft.com/windows");
    public static readonly SourceRef GameMode = new("Xbox Support: Game Mode on Windows", "https://support.xbox.com/help/games-apps/game-setup-and-play/use-game-mode-gaming-on-pc");
    public static readonly SourceRef RiotValorantSupport = new("Riot Games Support: VALORANT", "https://support-valorant.riotgames.com/");
    public static readonly SourceRef RiotMultithreaded = new("Riot Games Support: Multithreaded Rendering", "https://support-valorant.riotgames.com/hc/en-us/articles/4408444846355-Multithreaded-Rendering");
    public static readonly SourceRef MicrosoftDisplayRefresh = new("Microsoft Support: Change the refresh rate on your monitor", "https://support.microsoft.com/windows");
}

/// <summary>Convenience base that builds recommendations with the rule's metadata filled in.</summary>
public abstract class RuleBase : IRule
{
    public abstract RuleMetadata Meta { get; }
    public abstract Recommendation? Evaluate(RuleContext context);

    protected Recommendation Make(
        Confidence confidence,
        LocalizedText title,
        LocalizedText symptom,
        IReadOnlyList<Evidence> evidence,
        LocalizedText causes,
        LocalizedText action,
        LocalizedText? verification = null,
        GameId? game = null,
        IReadOnlyDictionary<string, string>? actionParameters = null,
        RecommendationKind? kindOverride = null) =>
        new(Meta.Id, Meta.Version, kindOverride ?? Meta.Mode, confidence, Meta.Risk, title, symptom, evidence, causes, action,
            Meta.Limitations, verification, Meta.Sources, (kindOverride ?? Meta.Mode) == RecommendationKind.AutoApplicable ? Meta.ActionId : null,
            actionParameters, Meta.RequiresElevation, Meta.RequiresRestart, game);

    protected static Evidence Ev(string ru, string en, string value, string source) => new(new LocalizedText(ru, en), value, source);

    protected static readonly DateOnly Validated = new(2026, 10, 25);
    protected static readonly IReadOnlyList<GameId> AnyGame = Array.Empty<GameId>();
    protected static readonly IReadOnlyList<GameId> BothGames = new[] { GameId.Cs2, GameId.Valorant };
}

public static class RuleEngine
{
    public static IReadOnlyList<Recommendation> Evaluate(IEnumerable<IRule> rules, RuleContext context)
    {
        var list = new List<Recommendation>();
        foreach (var rule in rules)
        {
            var m = rule.Meta;
            if (context.Snapshot.Os.Build.IsAvailable && context.Snapshot.Os.Build.Value < m.MinWindowsBuild) continue;
            if (m.SupportedGames.Count > 0 && (context.TargetGame is null || !m.SupportedGames.Contains(context.TargetGame.Value))) continue;
            Recommendation? rec;
            try { rec = rule.Evaluate(context); }
            catch { rec = null; }
            if (rec is not null) list.Add(rec);
        }
        return list
            .OrderBy(r => r.Kind)
            .ThenByDescending(r => r.Confidence)
            .ToList();
    }
}
