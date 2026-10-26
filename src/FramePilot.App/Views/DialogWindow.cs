using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace FramePilot.App.Views;

public enum DialogKind
{
    Info,
    Warning,
    Error,
    Confirm
}

/// <summary>Brand-styled modal dialog built in code so it works before any page is loaded.</summary>
public sealed class DialogWindow : Window
{
    private DialogWindow(string title, string message, DialogKind kind, string okText, string? cancelText)
    {
        Title = title;
        Width = 480;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = (Brush)Application.Current.Resources["Bg"];
        FontFamily = (FontFamily)Application.Current.Resources["UiFont"];
        var owner = Application.Current.MainWindow;
        if (owner is not null && owner.IsVisible && !ReferenceEquals(owner, this)) Owner = owner;

        var accent = kind switch
        {
            DialogKind.Warning => (Brush)Application.Current.Resources["Warn"],
            DialogKind.Error => (Brush)Application.Current.Resources["Danger"],
            _ => (Brush)Application.Current.Resources["Accent"]
        };

        var root = new StackPanel { Margin = new Thickness(24) };
        root.Children.Add(new Border { Height = 3, Width = 40, Background = accent, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 14), CornerRadius = new CornerRadius(0) });
        root.Children.Add(new TextBlock { Text = title, Style = (Style)Application.Current.Resources["H2"], Margin = new Thickness(0, 0, 0, 10) });
        root.Children.Add(new TextBlock { Text = message, Style = (Style)Application.Current.Resources["Body"] });

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 22, 0, 0) };
        if (cancelText is not null)
        {
            var cancel = new Button { Content = cancelText, IsCancel = true };
            cancel.Click += (_, _) => { DialogResult = false; };
            buttons.Children.Add(cancel);
        }
        var ok = new Button { Content = okText, IsDefault = true, Style = (Style)Application.Current.Resources["PrimaryButton"], Margin = new Thickness(0) };
        ok.Click += (_, _) => { DialogResult = true; };
        buttons.Children.Add(ok);
        root.Children.Add(buttons);
        Content = root;

        Loaded += (_, _) =>
        {
            DarkTitleBar.Apply(this);
            ok.Focus();
        };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { DialogResult = cancelText is null; e.Handled = true; }
        };
    }

    public static void Show(string title, string message, DialogKind kind = DialogKind.Info)
    {
        var d = new DialogWindow(title, message, kind, Localization.Loc.T("Ok"), null);
        d.ShowDialog();
    }

    public static bool Confirm(string title, string message, string yes, string no)
    {
        var d = new DialogWindow(title, message, DialogKind.Confirm, yes, no);
        return d.ShowDialog() == true;
    }
}
