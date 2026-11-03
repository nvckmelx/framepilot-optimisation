using System.Windows.Input;
using FramePilot.App.Localization;
using FramePilot.App.Services;

namespace FramePilot.App.ViewModels;

public sealed class NavItem : ObservableObject
{
    private bool _isSelected;
    public NavItem(string key, string icon, object page)
    {
        Key = key;
        Icon = icon;
        Page = page;
    }

    public string Key { get; }
    public string Icon { get; }
    public object Page { get; }
    public string Title => Loc.T(Key);

    public bool IsSelected
    {
        get => _isSelected;
        set => Set(ref _isSelected, value);
    }

    public void Refresh() => Raise(nameof(Title));
}

public sealed class MainViewModel : ObservableObject
{
    private object _currentPage;

    public Workspace Workspace { get; }
    public OverviewViewModel Overview { get; }
    public HardwareViewModel Hardware { get; }
    public GamesViewModel Games { get; }
    public SessionsViewModel Sessions { get; }
    public RecommendationsViewModel Recommendations { get; }
    public HistoryViewModel History { get; }
    public RecoveryViewModel Recovery { get; }
    public SettingsViewModel Settings { get; }
    public ComputerPage Computer { get; }
    public ChangesPage Changes { get; }
    public IReadOnlyList<NavItem> Nav { get; }
    public ICommand NavigateCommand { get; }

    public MainViewModel(AppServices app)
    {
        Workspace = new Workspace(app);
        Overview = new OverviewViewModel(this);
        Hardware = new HardwareViewModel(Workspace);
        Games = new GamesViewModel(this);
        Sessions = new SessionsViewModel(this);
        Recommendations = new RecommendationsViewModel(this);
        History = new HistoryViewModel(this);
        Recovery = new RecoveryViewModel(this);
        Settings = new SettingsViewModel(this);
        Computer = new ComputerPage(Hardware, Games);
        Changes = new ChangesPage(History, Recovery);

        // Six sections grouped by task (spec 2026-11-04 §4). Icons: 24×24 stroke paths.
        Nav = new[]
        {
            new NavItem("Nav_Overview", "M3,12 L12,4 L21,12 M6,10 V20 H18 V10", Overview),
            new NavItem("Nav_Sessions", "M12,8 V12 L15,14 M12,21 A9,9 0 1 0 12,3 A9,9 0 0 0 12,21", Sessions),
            new NavItem("Nav_Recommendations", "M9,6 H20 M9,12 H20 M9,18 H20 M4,6 L5,7 L7,5 M4,12 L5,13 L7,11 M4,18 H6", Recommendations),
            new NavItem("Nav_History", "M4,12 A8,8 0 1 1 7,18.2 M4,12 L1.5,9.5 M4,12 L6.5,9.5", Changes),
            new NavItem("Nav_Hardware", "M4,5 H20 V15 H4 Z M9,19 H15 M12,15 V19", Computer),
            new NavItem("Nav_Settings", "M12,9 A3,3 0 1 0 12,15 A3,3 0 1 0 12,9 M12,2 V5 M12,19 V22 M2,12 H5 M19,12 H22 M5,5 L7,7 M17,17 L19,19 M5,19 L7,17 M17,7 L19,5", Settings)
        };
        NavigateCommand = new RelayCommand(p => { if (p is NavItem n) Navigate(n.Page); });
        _currentPage = Overview;
        Nav[0].IsSelected = true;

        Loc.Instance.LanguageChanged += (_, _) =>
        {
            foreach (var n in Nav) n.Refresh();
            Workspace.NotifyChanged();
        };
    }

    public object CurrentPage
    {
        get => _currentPage;
        private set => Set(ref _currentPage, value);
    }

    public bool IsDemo => Workspace.IsDemo;

    public void Navigate(object page)
    {
        // Pages that now live inside a combined section open that section.
        if (ReferenceEquals(page, Hardware) || ReferenceEquals(page, Games)) page = Computer;
        else if (ReferenceEquals(page, History) || ReferenceEquals(page, Recovery)) page = Changes;
        CurrentPage = page;
        foreach (var n in Nav) n.IsSelected = ReferenceEquals(n.Page, page);
        if (page is IPage p) p.OnNavigatedTo();
    }

    public void Initialize()
    {
        Workspace.LoadPersisted();
        Raise(nameof(IsDemo));
    }

    public void RefreshDemoState()
    {
        Workspace.LoadPersisted();
        Raise(nameof(IsDemo));
    }
}

public interface IPage
{
    void OnNavigatedTo();
}

/// <summary>"Компьютер": hardware inventory and detected games on one page.</summary>
public sealed class ComputerPage : IPage
{
    public ComputerPage(HardwareViewModel hardware, GamesViewModel games) { Hardware = hardware; Games = games; }
    public HardwareViewModel Hardware { get; }
    public GamesViewModel Games { get; }
    public void OnNavigatedTo() { Hardware.OnNavigatedTo(); Games.OnNavigatedTo(); }
}

/// <summary>"Изменения": the change journal and recovery tools on one page.</summary>
public sealed class ChangesPage : IPage
{
    public ChangesPage(HistoryViewModel history, RecoveryViewModel recovery) { History = history; Recovery = recovery; }
    public HistoryViewModel History { get; }
    public RecoveryViewModel Recovery { get; }
    public void OnNavigatedTo() { History.OnNavigatedTo(); Recovery.OnNavigatedTo(); }
}
