namespace FramePilot.Core.Changes;

/// <summary>Identifiers of the only actions FramePilot can apply. Anything else is shown as an instruction.</summary>
public static class ActionIds
{
    public const string EnableGameMode = "windows.game-mode.enable";
    public const string EnableWindowedOptimizations = "windows.windowed-game-optimizations.enable";
    public const string SetHighPerformanceGpu = "windows.app-gpu-preference.high-performance";

    /// <summary>Parameter: full path to the game executable for per-app GPU preference.</summary>
    public const string ParamExecutablePath = "exePath";
}
