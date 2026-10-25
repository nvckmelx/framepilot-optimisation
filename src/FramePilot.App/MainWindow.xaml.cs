using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using FramePilot.App.ViewModels;
using FramePilot.App.Views;

namespace FramePilot.App;

public partial class MainWindow : Window
{
    private const int HotkeyId = 0x4650; // "FP"
    private const int WM_HOTKEY = 0x0312;
    private const uint MOD_CONTROL = 0x0002, MOD_SHIFT = 0x0004, MOD_NOREPEAT = 0x4000;
    private const uint VK_F10 = 0x79;
    private HwndSource? _source;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    public MainWindow()
    {
        InitializeComponent();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        DarkTitleBar.Apply(this);
        var handle = new WindowInteropHelper(this).Handle;
        _source = HwndSource.FromHwnd(handle);
        _source?.AddHook(WndProc);
        // Global hotkey Ctrl+Shift+F10 stops an in-progress measurement while the game has focus.
        RegisterHotKey(handle, HotkeyId, MOD_CONTROL | MOD_SHIFT | MOD_NOREPEAT, VK_F10);
    }

    protected override void OnClosed(EventArgs e)
    {
        var handle = new WindowInteropHelper(this).Handle;
        UnregisterHotKey(handle, HotkeyId);
        _source?.RemoveHook(WndProc);
        if (DataContext is MainViewModel vm && vm.Workspace.Capture.IsCapturing) vm.Workspace.Capture.RequestStop();
        base.OnClosed(e);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && wParam.ToInt32() == HotkeyId && DataContext is MainViewModel vm)
        {
            if (vm.Sessions.StopCommand.CanExecute(null)) vm.Sessions.StopCommand.Execute(null);
            handled = true;
        }
        return IntPtr.Zero;
    }
}
