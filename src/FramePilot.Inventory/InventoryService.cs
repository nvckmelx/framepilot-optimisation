using FramePilot.Core;
using FramePilot.Core.Models;

namespace FramePilot.Inventory;

public interface IInventoryService
{
    Task<SystemSnapshot> CollectAsync(Func<IReadOnlyList<DetectedGame>> detectGames, TimeSpan observation, IProgress<string>? progress, CancellationToken ct);
}

/// <summary>Collects a real, read-only snapshot of the PC. Every missing value is reported with its reason.</summary>
public sealed class InventoryService : IInventoryService
{
    public async Task<SystemSnapshot> CollectAsync(Func<IReadOnlyList<DetectedGame>> detectGames, TimeSpan observation,
        IProgress<string>? progress, CancellationToken ct)
    {
        var errors = new List<string>();

        progress?.Report("os");
        var os = await Task.Run(() => HardwareCollectors.CollectOs(errors), ct).ConfigureAwait(false);
        progress?.Report("cpu");
        var cpu = await Task.Run(() => HardwareCollectors.CollectCpu(errors), ct).ConfigureAwait(false);
        progress?.Report("gpu");
        var gpus = await Task.Run(() => HardwareCollectors.CollectGpus(errors), ct).ConfigureAwait(false);
        progress?.Report("memory");
        var mem = await Task.Run(() => HardwareCollectors.CollectMemory(errors), ct).ConfigureAwait(false);
        progress?.Report("storage");
        var disks = await Task.Run(() => HardwareCollectors.CollectDisks(errors), ct).ConfigureAwait(false);
        progress?.Report("displays");
        var displays = await Task.Run(() => HardwareCollectors.CollectDisplays(errors), ct).ConfigureAwait(false);
        progress?.Report("settings");
        var settings = await Task.Run(() => SettingsCollector.Collect(errors), ct).ConfigureAwait(false);
        progress?.Report("games");
        IReadOnlyList<DetectedGame> games;
        try { games = await Task.Run(detectGames, ct).ConfigureAwait(false); }
        catch (Exception ex) { errors.Add("Games: " + ex.Message); games = Array.Empty<DetectedGame>(); }

        progress?.Report("background");
        BackgroundObservation background;
        Measured<double> load;
        try
        {
            (background, load) = await BackgroundObserver.ObserveAsync(observation, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            errors.Add("Background observation: " + ex.Message);
            load = Measured<double>.Missing(ex.Message, "GetSystemTimes");
            background = new BackgroundObservation(0, load, Array.Empty<BackgroundProcessSample>());
        }

        cpu = cpu with { LoadPercent = load };

        return new SystemSnapshot(SystemSnapshot.CurrentSchema, Guid.NewGuid(), DateTimeOffset.Now, false,
            os, cpu, gpus, mem, disks, displays, settings, background, games, errors);
    }
}
