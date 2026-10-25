using System.IO;
using System.Windows.Input;
using FramePilot.App.Localization;
using FramePilot.App.Services;
using FramePilot.Core.Models;
using FramePilot.GameAdapters;

namespace FramePilot.App.ViewModels;

public sealed class GameCard
{
    public required GameId Game { get; init; }
    public required string Name { get; init; }
    public required string State { get; init; }
    public required bool Installed { get; init; }
    public required bool IsTarget { get; init; }
    public required IReadOnlyList<InfoRow> Rows { get; init; }
    public required ICommand SelectCommand { get; init; }
    public required ICommand OpenFolderCommand { get; init; }
}

public sealed class GamesViewModel : ObservableObject, IPage
{
    private readonly MainViewModel _main;

    public GamesViewModel(MainViewModel main)
    {
        _main = main;
        RescanCommand = new AsyncCommand(() => _main.Workspace.RunInventoryAsync(), () => !_main.Workspace.Busy);
        _main.Workspace.Changed += (_, _) => Raise(nameof(Cards));
    }

    public ICommand RescanCommand { get; }

    public void OnNavigatedTo() => Raise(nameof(Cards));

    public IReadOnlyList<GameCard> Cards
    {
        get
        {
            var snap = _main.Workspace.Snapshot;
            var list = new List<GameCard>();
            foreach (var id in new[] { GameId.Cs2, GameId.Valorant })
            {
                var g = snap?.Games.FirstOrDefault(x => x.Game == id);
                var rows = new List<InfoRow>();
                if (g is null)
                {
                    rows.Add(new InfoRow(Loc.T("Games_Status"), Loc.T("Games_NotScanned"), false, ""));
                }
                else
                {
                    rows.Add(new InfoRow(Loc.T("Games_Folder"), g.InstallPath ?? Loc.T("Unavailable"), g.InstallPath is not null, g.DetectionSource));
                    rows.Add(new InfoRow(Loc.T("Games_Exe"), g.ExecutablePath ?? Loc.T("Unavailable"), g.ExecutablePath is not null, ""));
                    rows.Add(new InfoRow(Loc.T("Games_Version"), g.Version.IsAvailable ? g.Version.Value! : Loc.T("Unavailable"), g.Version.IsAvailable,
                        g.Version.IsAvailable ? g.Version.Source : g.Version.UnavailableReason ?? ""));
                    rows.Add(new InfoRow(Loc.T("Games_Config"), g.ConfigSummary.IsAvailable ? g.ConfigSummary.Value! : Loc.T("Unavailable"), g.ConfigSummary.IsAvailable,
                        g.ConfigSummary.IsAvailable ? g.ConfigSummary.Source + " · " + g.ConfigSummary.Note : g.ConfigSummary.UnavailableReason ?? ""));
                    rows.Add(new InfoRow(Loc.T("Games_Source"), g.DetectionSource, true, ""));
                }
                var folder = g?.InstallPath;
                list.Add(new GameCard
                {
                    Game = id,
                    Name = GameCatalog.DisplayName(id),
                    Installed = g?.Installed == true,
                    State = g is null ? Loc.T("Games_NotScanned")
                        : !g.Installed ? Loc.T("Games_NotInstalled")
                        : GameCatalog.IsRunning(id) ? Loc.T("Games_Running") : Loc.T("Games_Installed"),
                    IsTarget = _main.Workspace.TargetGame == id,
                    Rows = rows,
                    SelectCommand = new RelayCommand(() => { _main.Workspace.TargetGame = id; Raise(nameof(Cards)); }),
                    OpenFolderCommand = new RelayCommand(() => { if (folder is not null && Directory.Exists(folder)) Shell.Open(folder); }, () => folder is not null)
                });
            }
            return list;
        }
    }
}
