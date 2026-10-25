namespace FramePilot.Core.Models;

/// <summary>The player's stated priority. It changes which findings are relevant (e.g. a frame cap is fine for smoothness).</summary>
public enum PerformanceGoal
{
    StableFps,
    MinimalLatency
}

public enum Confidence
{
    Low,
    Medium,
    High
}

public enum RiskLevel
{
    None,
    Low,
    Medium,
    High
}

/// <summary>The three plan categories from the product spec.</summary>
public enum RecommendationKind
{
    /// <summary>A tested, reversible FramePilot action exists. Still requires explicit approval.</summary>
    AutoApplicable,
    /// <summary>The user must decide or act (in-game setting, BIOS, Windows Settings page).</summary>
    UserAction,
    /// <summary>Information or a hypothesis that needs more testing; nothing to apply.</summary>
    DiagnosticOnly
}

public sealed record LocalizedText(string Ru, string En)
{
    public string Get(string language) => language.StartsWith("ru", StringComparison.OrdinalIgnoreCase) ? Ru : En;
    public static LocalizedText Same(string text) => new(text, text);
}

public sealed record SourceRef(string Title, string Url);

public sealed record Evidence(LocalizedText Label, string Value, string Source);

/// <summary>A rule outcome: symptom, evidence, causes, confidence, action and the limits of the conclusion.</summary>
public sealed record Recommendation(
    string RuleId,
    int RuleVersion,
    RecommendationKind Kind,
    Confidence Confidence,
    RiskLevel Risk,
    LocalizedText Title,
    LocalizedText Symptom,
    IReadOnlyList<Evidence> Evidence,
    LocalizedText PossibleCauses,
    LocalizedText Action,
    LocalizedText Limitations,
    LocalizedText? Verification,
    IReadOnlyList<SourceRef> Sources,
    string? ActionId,
    IReadOnlyDictionary<string, string>? ActionParameters,
    bool RequiresElevation,
    bool RequiresRestart,
    GameId? Game);

/// <summary>Rule metadata required by docs/RULES_POLICY.md. The catalog is data plus identifiers of implemented actions, never shell commands.</summary>
public sealed record RuleMetadata(
    string Id,
    int Version,
    int MinWindowsBuild,
    IReadOnlyList<GameId> SupportedGames,
    string HardwareConditions,
    string RequiredEvidence,
    RiskLevel Risk,
    bool RequiresElevation,
    bool RequiresRestart,
    RecommendationKind Mode,
    string? ActionId,
    IReadOnlyList<SourceRef> Sources,
    DateOnly LastValidatedAt,
    LocalizedText Limitations);
