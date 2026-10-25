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

        // Icon geometry: simple 24×24 stroke paths drawn for FramePilot.
        Nav = new[]
        {
            new NavItem("Nav_Overview", "M3,12 L12,4 L21,12 M6,10 V20 H18 V10", Overview),
            new NavItem("Nav_Hardware", "M4,5 H20 V15 H4 Z M9,19 H15 M12,15 V19", Hardware),
            new NavItem("Nav_Games", "M6,9 H18 A3,3 0 0 1 21,12 V14 A3,3 0 0 1 15,16 L14,15 H10 L9,16 A3,3 0 0 1 3,14 V12 A3,3 0 0 1 6,9 Z M7,12 H10 M8.5,10.5 V13.5", Games),
            new NavItem("Nav_Sessions", "M3,17 L8,11 L12,14 L17,7 L21,10 M3,20 H21", Sessions),
            new NavItem("Nav_Recommendations", "M12,3 L14.5,9 L21,9.5 L16,13.5 L17.5,20 L12,16.5 L6.5,20 L8,13.5 L3,9.5 L9.5,9 Z", Recommendations),
            new NavItem("Nav_History", "M12,7 V12 L15,14 M3.5,12 A8.5,8.5 0 1 0 6,6 M3,3 V7 H7", History),
            new NavItem("Nav_Recovery", "M4,12 A8,8 0 1 1 7,18 M4,12 L1.5,9.5 M4,12 L6.5,9.5", Recovery),
            new NavItem("Nav_Settings", "M12,8 A4,4 0 1 1 11.99,8 M12,2 V5 M12,19 V22 M2,12 H5 M19,12 H22 M4.9,4.9 L7,7 M17,17 L19.1,19.1 M4.9,19.1 L7,17 M17,7 L19.1,4.9", Settings)
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
