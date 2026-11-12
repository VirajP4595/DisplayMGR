using System.Collections.ObjectModel;
using System.Windows.Input;
using DisplayMGR.Monitors;
using DisplayMGR.Services;

namespace DisplayMGR.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly AppSettings _settings;
    private bool _refreshing;
    private bool _refreshAgain;
    private bool _applyingUnison;
    private bool _isScanning;
    private bool _startWithWindows;

    public MainViewModel(AppSettings settings)
    {
        _settings = settings;
        _startWithWindows = StartupRegistration.IsEnabled;
        RescanCommand = new RelayCommand(() => _ = RefreshAsync(forgetCapabilities: true));
        ShowPanelCommand = new RelayCommand(() => ShowPanelRequested?.Invoke());
        HidePanelCommand = new RelayCommand(() => HidePanelRequested?.Invoke());
        HideWidgetCommand = new RelayCommand(() => WidgetVisible = false);
        ExitCommand = new RelayCommand(() => ExitRequested?.Invoke());
    }

    public ObservableCollection<MonitorViewModel> Monitors { get; } = new();

    /// <summary>Asks the user to confirm a destructive action. Set by the view.</summary>
    public Func<string, bool> Confirm { get; set; } = _ => true;

    public event Action? ShowPanelRequested;
    public event Action? HidePanelRequested;
    public event Action? ExitRequested;

    public ICommand RescanCommand { get; }
    public ICommand ShowPanelCommand { get; }
    public ICommand HidePanelCommand { get; }
    public ICommand HideWidgetCommand { get; }
    public ICommand ExitCommand { get; }

    public bool IsScanning { get => _isScanning; private set => Set(ref _isScanning, value); }
    public bool HasNoMonitors => !IsScanning && Monitors.Count == 0;

    public bool Unison
    {
        get => _settings.Unison;
        set { if (_settings.Unison != value) { _settings.Unison = value; Changed(); } }
    }

    public bool KeepPanelOpen
    {
        get => _settings.KeepPanelOpen;
        set { if (_settings.KeepPanelOpen != value) { _settings.KeepPanelOpen = value; Changed(); } }
    }

    public bool WidgetVisible
    {
        get => _settings.WidgetVisible;
        set { if (_settings.WidgetVisible != value) { _settings.WidgetVisible = value; Changed(); } }
    }

    public bool WidgetTopmost
    {
        get => _settings.WidgetTopmost;
        set { if (_settings.WidgetTopmost != value) { _settings.WidgetTopmost = value; Changed(); } }
    }

    public bool WidgetShowInputs
    {
        get => _settings.WidgetShowInputs;
        set { if (_settings.WidgetShowInputs != value) { _settings.WidgetShowInputs = value; Changed(); } }
    }

    public bool StartWithWindows
    {
        get => _startWithWindows;
        set
        {
            if (_startWithWindows == value)
                return;
            StartupRegistration.Set(value);
            _startWithWindows = StartupRegistration.IsEnabled;
            OnPropertyChanged();
        }
    }

    /// <summary>Re-detects the monitors. Safe to call while a scan is already running.</summary>
    public async Task RefreshAsync(bool forgetCapabilities = false)
    {
        if (forgetCapabilities)
            _settings.CapabilitiesCache.Clear();
        if (_refreshing)
        {
            _refreshAgain = true;
            return;
        }

        _refreshing = true;
        try
        {
            do
            {
                _refreshAgain = false;
                await ScanAsync();
            } while (_refreshAgain);
        }
        catch (Exception exception)
        {
            SettingsStore.Log(exception);
        }
        finally
        {
            _refreshing = false;
        }
    }

    public Task RefreshValuesAsync() =>
        _refreshing ? Task.CompletedTask : Task.WhenAll(Monitors.Select(m => m.RefreshValuesAsync()));

    private async Task ScanAsync()
    {
        IsScanning = true;
        OnPropertyChanged(nameof(HasNoMonitors));

        foreach (var old in Monitors)
            old.Dispose();
        Monitors.Clear();

        var detected = await Task.Run(MonitorEnumerator.Enumerate);
        foreach (var monitor in detected)
            Monitors.Add(new MonitorViewModel(monitor, message => Confirm(message)));

        IsScanning = false;
        OnPropertyChanged(nameof(HasNoMonitors));

        var loading = Monitors.ToList();
        await Task.WhenAll(loading.Select(async monitor =>
        {
            _settings.CapabilitiesCache.TryGetValue(monitor.Id, out string? cached);
            await monitor.LoadAsync(cached);
            if (monitor.Brightness is { } brightness)
                brightness.UserChanged += OnBrightnessChangedByUser;
            if (!string.IsNullOrEmpty(monitor.Capabilities))
                _settings.CapabilitiesCache[monitor.Id] = monitor.Capabilities;
        }));
        SettingsStore.Save(_settings);
    }

    private void OnBrightnessChangedByUser(VcpSliderViewModel source)
    {
        if (!Unison || _applyingUnison)
            return;
        _applyingUnison = true;
        try
        {
            foreach (var monitor in Monitors)
            {
                if (monitor.Brightness is { } other && !ReferenceEquals(other, source))
                    other.SetFraction(source.Fraction);
            }
        }
        finally
        {
            _applyingUnison = false;
        }
    }

    private void Changed([System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    {
        OnPropertyChanged(name);
        SettingsStore.Save(_settings);
    }
}
