using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;
using DisplayMGR.Services;
using DisplayMGR.ViewModels;
using DisplayMGR.Views;
using Microsoft.Win32;

namespace DisplayMGR;

public partial class App : Application
{
    private const string InstanceName = "DisplayMGR.SingleInstance";

    private Mutex? _mutex;
    private EventWaitHandle? _showSignal;
    private AppSettings _settings = new();
    private MainViewModel? _viewModel;
    private MainWindow? _panel;
    private WidgetWindow? _widget;
    private TrayIcon? _tray;
    private DispatcherTimer? _displayChangeTimer;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _mutex = new Mutex(initiallyOwned: true, InstanceName, out bool isFirstInstance);
        _showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, InstanceName + ".Show");
        if (!isFirstInstance)
        {
            // Already running: ask that instance to show its panel, then leave.
            _showSignal.Set();
            Shutdown();
            return;
        }

        DispatcherUnhandledException += (_, args) =>
        {
            SettingsStore.Log(args.Exception);
            args.Handled = true;
        };

        _settings = SettingsStore.Load();
        _viewModel = new MainViewModel(_settings);
        _viewModel.ExitRequested += Quit;
        _viewModel.PropertyChanged += OnViewModelChanged;

        _panel = new MainWindow(_viewModel);
        _tray = new TrayIcon(_viewModel, () => _panel.Toggle());
        UpdateWidget();

        // Monitor handles go stale when displays are plugged, unplugged or woken; rescan once things settle.
        _displayChangeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _displayChangeTimer.Tick += (_, _) =>
        {
            _displayChangeTimer.Stop();
            _ = _viewModel.RefreshAsync();
        };
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;

        var signal = _showSignal;
        var listener = new Thread(() =>
        {
            while (signal.WaitOne())
                Dispatcher.BeginInvoke(() => _panel?.ShowPanel());
        })
        { IsBackground = true, Name = "Show-panel listener" };
        listener.Start();

        _ = _viewModel.RefreshAsync();

        bool startedByWindows = e.Args.Contains(StartupRegistration.TrayArgument, StringComparer.OrdinalIgnoreCase);
        if (!startedByWindows)
            _panel.ShowPanel();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        _tray?.Dispose();
        if (_viewModel is not null)
        {
            foreach (var monitor in _viewModel.Monitors)
                monitor.Dispose();
        }
        base.OnExit(e);
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e) =>
        Dispatcher.BeginInvoke(() =>
        {
            _displayChangeTimer?.Stop();
            _displayChangeTimer?.Start();
        });

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.WidgetVisible))
            UpdateWidget();
    }

    private void UpdateWidget()
    {
        if (_viewModel is null)
            return;
        if (_viewModel.WidgetVisible)
        {
            _widget ??= new WidgetWindow(_viewModel, _settings);
            _widget.Show();
        }
        else
        {
            _widget?.Hide();
        }
    }

    private void Quit()
    {
        if (_panel is not null)
            _panel.AllowClose = true;
        if (_widget is not null)
            _widget.AllowClose = true;
        Shutdown();
    }
}
