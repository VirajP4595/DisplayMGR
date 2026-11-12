using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using DisplayMGR.Native;
using DisplayMGR.Services;
using DisplayMGR.ViewModels;

namespace DisplayMGR.Views;

/// <summary>
/// The desktop widget: a small borderless window that either sits on the desktop,
/// underneath every other window, or stays on top of them.
/// </summary>
public partial class WidgetWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly AppSettings _settings;
    private readonly DispatcherTimer _saveTimer;
    private IntPtr _hwnd;

    public WidgetWindow(MainViewModel viewModel, AppSettings settings)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _settings = settings;
        DataContext = viewModel;

        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
        _saveTimer.Tick += (_, _) =>
        {
            _saveTimer.Stop();
            _settings.WidgetLeft = Left;
            _settings.WidgetTop = Top;
            SettingsStore.Save(_settings);
        };

        RestorePosition();
        LocationChanged += (_, _) =>
        {
            _saveTimer.Stop();
            _saveTimer.Start();
        };
        viewModel.PropertyChanged += OnViewModelChanged;
    }

    /// <summary>Set before shutting down; otherwise closing only hides the widget.</summary>
    public bool AllowClose { get; set; }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _hwnd = new WindowInteropHelper(this).Handle;

        // A tool window stays out of Alt+Tab and the taskbar.
        int style = NativeMethods.GetWindowLong(_hwnd, NativeMethods.GWL_EXSTYLE);
        NativeMethods.SetWindowLong(_hwnd, NativeMethods.GWL_EXSTYLE, style | NativeMethods.WS_EX_TOOLWINDOW);

        HwndSource.FromHwnd(_hwnd)?.AddHook(WindowProc);
        ApplyLayer();
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        // Drag from the background only; sliders and buttons keep their own mouse handling.
        if (e.ButtonState == MouseButtonState.Pressed && !IsInteractive(e.OriginalSource as DependencyObject))
            DragMove();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!AllowClose)
        {
            e.Cancel = true;
            _viewModel.WidgetVisible = false;
        }
        base.OnClosing(e);
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.WidgetTopmost))
            ApplyLayer();
    }

    private void ApplyLayer()
    {
        Topmost = _viewModel.WidgetTopmost;
        if (!Topmost && _hwnd != IntPtr.Zero)
        {
            NativeMethods.SetWindowPos(_hwnd, NativeMethods.HWND_BOTTOM, 0, 0, 0, 0,
                NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
        }
    }

    private IntPtr WindowProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        // While pinned to the desktop, refuse every attempt to raise the window.
        if (message == NativeMethods.WM_WINDOWPOSCHANGING && !_viewModel.WidgetTopmost)
        {
            var position = Marshal.PtrToStructure<NativeMethods.WINDOWPOS>(lParam);
            position.hwndInsertAfter = NativeMethods.HWND_BOTTOM;
            position.flags &= ~NativeMethods.SWP_NOZORDER;
            Marshal.StructureToPtr(position, lParam, false);
        }
        return IntPtr.Zero;
    }

    private void RestorePosition()
    {
        double left = SystemParameters.VirtualScreenLeft;
        double top = SystemParameters.VirtualScreenTop;
        double right = left + SystemParameters.VirtualScreenWidth;
        double bottom = top + SystemParameters.VirtualScreenHeight;

        // Ignore a saved position that is no longer on any screen (a monitor was unplugged).
        if (_settings.WidgetLeft is double x && _settings.WidgetTop is double y &&
            x >= left && x <= right - 80 && y >= top && y <= bottom - 40)
        {
            Left = x;
            Top = y;
        }
        else
        {
            var area = SystemParameters.WorkArea;
            Left = area.Right - Width - 24;
            Top = area.Top + 24;
        }
    }

    private static bool IsInteractive(DependencyObject? element)
    {
        while (element is not null)
        {
            if (element is ButtonBase or Thumb or RangeBase)
                return true;
            element = element is System.Windows.Media.Visual
                ? System.Windows.Media.VisualTreeHelper.GetParent(element)
                : LogicalTreeHelper.GetParent(element);
        }
        return false;
    }
}
