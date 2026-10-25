using System.Collections.ObjectModel;
using FramePilot.App.Localization;
using FramePilot.App.Services;
using FramePilot.Core.Models;
using FramePilot.Rules;

namespace FramePilot.App.ViewModels;

/// <summary>Shared state for all pages: current snapshot, sessions, recommendations, target game and goal.</summary>
public sealed class Workspace : ObservableObject
{
    public AppServices App { get; }
    public CaptureService Capture { get; }

    private SystemSnapshot? _snapshot;
    private IReadOnlyList<Recommendation> _recommendations = Array.Empty<Recommendation>();
    private bool _busy;
    private string? _busyText;

    public Workspace(AppServices app)
    {
        App = app;
        Capture = new CaptureService(app);
    }

    public event EventHandler? Changed;

    public ObservableCollection<SessionRecord> Sessions { get; } = new();

    public bool IsDemo => App.Settings.DemoMode;

    public SystemSnapshot? Snapshot
    {
        get => _snapshot;
        private set => Set(ref _snapshot, value);
    }

    public IReadOnlyList<Recommendation> Recommendations
    {
        get => _recommendations;
        private set => Set(ref _recommendations, value);
    }

    public bool Busy
    {
        get => _busy;
        set => Set(ref _busy, value);
    }

    public string? BusyText
    {
        get => _busyText;
        set => Set(ref _busyText, value);
    }

    public GameId TargetGame
    {
        get => App.Settings.TargetGame;
        set
        {
            if (App.Settings.TargetGame == value) return;
            App.Settings.TargetGame = value;
            Raise();
            Evaluate();
        }
    }

    public PerformanceGoal Goal
    {
        get => App.Settings.Goal;
        set
        {
            if (App.Settings.Goal == value) return;
            App.Settings.Goal = value;
            Raise();
            Evaluate();
        }
    }

    public SessionRecord? LatestSessionForTarget =>
        Sessions.Where(s => s.Game == TargetGame && s.IsDemo == IsDemo).OrderByDescending(s => s.StartedAt).FirstOrDefault();

    public void LoadPersisted()
    {
        Sessions.Clear();
        if (IsDemo)
        {
            Snapshot = DemoData.Snapshot();
            foreach (var s in DemoData.Sessions().OrderByDescending(s => s.StartedAt)) Sessions.Add(s);
        }
        else
        {
            Snapshot = App.Store.LatestSnapshot(false);
            foreach (var s in App.Store.Sessions(false)) Sessions.Add(s);
        }
        Raise(nameof(IsDemo));
        Evaluate();
    }

    public async Task RunInventoryAsync()
    {
        if (Busy) return;
        Busy = true;
        try
        {
            if (IsDemo)
            {
                BusyText = Loc.T("Progress_demo");
                await Task.Delay(600);
                Snapshot = DemoData.Snapshot();
            }
            else
            {
                var progress = new Progress<string>(step => BusyText = Loc.T("Progress_" + step));
                var snap = await App.Inventory.CollectAsync(() => App.Games.DetectAll(), TimeSpan.FromSeconds(5), progress, CancellationToken.None);
                App.Store.SaveSnapshot(snap);
                App.Store.Audit("inventory", $"Snapshot {snap.Id} with {snap.CollectorErrors.Count} collector errors");
                Snapshot = snap;
            }
            Evaluate();
        }
        finally
        {
            Busy = false;
            BusyText = null;
        }
    }

    public void AddSession(SessionRecord session)
    {
        Sessions.Insert(0, session);
        Evaluate();
    }

    public void RemoveSession(SessionRecord session)
    {
        Sessions.Remove(session);
        if (!session.IsDemo) App.Store.DeleteSession(session.Id);
        Evaluate();
    }

    public void Evaluate()
    {
        Recommendations = Snapshot is null
            ? Array.Empty<Recommendation>()
            : RuleEngine.Evaluate(RuleCatalog.All, new RuleContext(Snapshot, TargetGame, LatestSessionForTarget, Goal));
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void NotifyChanged() => Changed?.Invoke(this, EventArgs.Empty);
}
