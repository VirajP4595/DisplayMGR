using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using DisplayMGR.Monitors;

namespace DisplayMGR.ViewModels;

public sealed class MonitorViewModel : ObservableObject, IDisposable
{
    private readonly IMonitorDevice _device;
    private readonly Func<string, bool> _confirm;

    private VcpSliderViewModel? _brightness;
    private VcpOptionGroupViewModel? _inputs;
    private string _status = "Reading monitor…";
    private string _subtitle;
    private string _capabilitiesText = "";
    private string _rawCode = "10";
    private string _rawValue = "";
    private string _rawResult = "";
    private bool _isExpanded;
    private bool _isMuted;
    private bool _hasMute;
    private bool _hasPower;
    private bool _hasFactoryReset;
    private bool _hasColorReset;

    public MonitorViewModel(DetectedMonitor monitor, Func<string, bool> confirm)
    {
        _device = monitor.Device;
        _confirm = confirm;
        Id = monitor.Id;
        Name = monitor.Name;
        _subtitle = monitor.IsInternal ? "Built-in panel" : "DDC/CI";

        StandbyCommand = new RelayCommand(() => SetPower(VcpCatalog.PowerStandby));
        PowerOffCommand = new RelayCommand(() =>
        {
            if (_confirm($"Turn off {Name}? You may need its power button to turn it back on."))
                SetPower(VcpCatalog.PowerOff);
        });
        FactoryResetCommand = new RelayCommand(() =>
        {
            if (_confirm($"Restore ALL factory settings on {Name}? This cannot be undone."))
                _ = ResetAsync(VcpCatalog.FactoryReset);
        });
        ColorResetCommand = new RelayCommand(() =>
        {
            if (_confirm($"Reset the colour settings on {Name}?"))
                _ = ResetAsync(VcpCatalog.ColorReset);
        });
        RawReadCommand = new RelayCommand(() => _ = RawReadAsync());
        RawWriteCommand = new RelayCommand(() => _ = RawWriteAsync());
        ToggleExpandedCommand = new RelayCommand(() => IsExpanded = !IsExpanded);
    }

    public string Id { get; }
    public string Name { get; }
    public bool SupportsVcp => _device.SupportsVcp;

    public string Subtitle { get => _subtitle; private set => Set(ref _subtitle, value); }
    public string Status { get => _status; private set { if (Set(ref _status, value)) OnPropertyChanged(nameof(HasStatus)); } }
    public bool HasStatus => Status.Length > 0;

    public VcpSliderViewModel? Brightness
    {
        get => _brightness;
        private set { if (Set(ref _brightness, value)) OnPropertyChanged(nameof(HasBrightness)); }
    }
    public bool HasBrightness => Brightness is not null;

    public VcpOptionGroupViewModel? Inputs
    {
        get => _inputs;
        private set { if (Set(ref _inputs, value)) OnPropertyChanged(nameof(HasInputs)); }
    }
    public bool HasInputs => Inputs is not null;

    public ObservableCollection<VcpSliderViewModel> ExtraSliders { get; } = new();
    public ObservableCollection<VcpOptionGroupViewModel> OptionGroups { get; } = new();

    public bool IsExpanded { get => _isExpanded; set => Set(ref _isExpanded, value); }

    public bool HasMute { get => _hasMute; private set => Set(ref _hasMute, value); }
    public bool IsMuted
    {
        get => _isMuted;
        set
        {
            if (Set(ref _isMuted, value))
                Task.Run(() => _device.TrySetVcp(VcpCatalog.Mute, value ? VcpCatalog.MuteOn : VcpCatalog.MuteOff));
        }
    }

    public bool HasPower { get => _hasPower; private set => Set(ref _hasPower, value); }
    public bool HasFactoryReset { get => _hasFactoryReset; private set => Set(ref _hasFactoryReset, value); }
    public bool HasColorReset { get => _hasColorReset; private set => Set(ref _hasColorReset, value); }

    public string CapabilitiesText { get => _capabilitiesText; private set => Set(ref _capabilitiesText, value); }
    public string RawCode { get => _rawCode; set => Set(ref _rawCode, value); }
    public string RawValue { get => _rawValue; set => Set(ref _rawValue, value); }
    public string RawResult { get => _rawResult; private set => Set(ref _rawResult, value); }

    public ICommand StandbyCommand { get; }
    public ICommand PowerOffCommand { get; }
    public ICommand FactoryResetCommand { get; }
    public ICommand ColorResetCommand { get; }
    public ICommand RawReadCommand { get; }
    public ICommand RawWriteCommand { get; }
    public ICommand ToggleExpandedCommand { get; }

    /// <summary>The capabilities string that was read, for caching. Null when there is none.</summary>
    public string? Capabilities { get; private set; }

    private sealed record ProbeResult(
        string? CapabilitiesString,
        MonitorCapabilities Capabilities,
        Dictionary<byte, (uint Current, uint Maximum)> Values);

    /// <summary>Reads what the monitor supports and builds its controls. Call from the UI thread.</summary>
    public async Task LoadAsync(string? cachedCapabilities)
    {
        // Brightness first: it is what people open the app for, and capabilities take seconds.
        var brightness = await Task.Run(() => Read(VcpCatalog.Brightness));
        if (brightness is var (current, maximum))
            Brightness = new VcpSliderViewModel(_device, "Brightness", VcpCatalog.Brightness, current, maximum);

        if (!_device.SupportsVcp)
        {
            Status = HasBrightness ? "" : "Windows does not expose brightness control for this panel.";
            return;
        }

        var probe = await Task.Run(() => Probe(cachedCapabilities));
        Capabilities = probe.CapabilitiesString;
        var caps = probe.Capabilities;
        bool hasCaps = caps.Vcp.Count > 0;

        foreach (var (code, name) in VcpCatalog.ExtraSliders)
        {
            if (probe.Values.TryGetValue(code, out var v) && v.Maximum > 0)
                ExtraSliders.Add(new VcpSliderViewModel(_device, name, code, v.Current, v.Maximum));
        }

        uint? CurrentOf(byte code) => probe.Values.TryGetValue(code, out var v) ? v.Current : null;

        var inputValues = caps.ValuesFor(VcpCatalog.InputSource);
        if (inputValues.Count == 0 && probe.Values.ContainsKey(VcpCatalog.InputSource))
            inputValues = VcpCatalog.FallbackInputs;
        if (inputValues.Count > 0)
            Inputs = new VcpOptionGroupViewModel(_device, "Input", VcpCatalog.InputSource, inputValues,
                VcpCatalog.InputName, CurrentOf(VcpCatalog.InputSource));

        if (caps.ValuesFor(VcpCatalog.DisplayMode) is { Count: > 0 } modes)
            OptionGroups.Add(new VcpOptionGroupViewModel(_device, "Picture mode", VcpCatalog.DisplayMode, modes,
                VcpCatalog.DisplayModeName, CurrentOf(VcpCatalog.DisplayMode)));

        if (caps.ValuesFor(VcpCatalog.ColorPreset) is { Count: > 0 } presets)
            OptionGroups.Add(new VcpOptionGroupViewModel(_device, "Colour preset", VcpCatalog.ColorPreset, presets,
                VcpCatalog.ColorPresetName, CurrentOf(VcpCatalog.ColorPreset)));

        if (probe.Values.TryGetValue(VcpCatalog.Mute, out var mute))
        {
            _isMuted = (mute.Current & 0xFF) == VcpCatalog.MuteOn;
            OnPropertyChanged(nameof(IsMuted));
            HasMute = true;
        }

        HasPower = probe.Values.ContainsKey(VcpCatalog.PowerMode);
        // Resets are write-only, so the capabilities string is the only evidence for them.
        HasFactoryReset = caps.Supports(VcpCatalog.FactoryReset);
        HasColorReset = caps.Supports(VcpCatalog.ColorReset);

        CapabilitiesText = probe.CapabilitiesString ?? "(the monitor did not return a capabilities string)";
        if (caps.Model.Length > 0)
            Subtitle = $"DDC/CI · {caps.Model}";

        bool anything = HasBrightness || HasInputs || ExtraSliders.Count > 0 || OptionGroups.Count > 0;
        Status = anything
            ? (hasCaps ? "" : "This monitor did not list its features, so the controls shown were found by probing.")
            : "No response over DDC/CI. Enable DDC/CI in the monitor's on-screen menu, or try another cable or port.";
    }

    /// <summary>Re-reads values that the monitor's own buttons may have changed.</summary>
    public async Task RefreshValuesAsync()
    {
        var brightness = Brightness;
        var inputs = Inputs;
        var (b, i) = await Task.Run(() => (
            brightness is null ? null : Read(VcpCatalog.Brightness),
            inputs is null ? null : Read(VcpCatalog.InputSource)));
        if (b is not null)
            brightness!.ShowDeviceValue(b.Value.Current);
        if (i is not null)
            inputs!.ShowDeviceValue(i.Value.Current);
    }

    public void Dispose() => _device.Dispose();

    private (uint Current, uint Maximum)? Read(byte code) =>
        _device.TryGetVcp(code, out uint current, out uint maximum) ? (current, maximum) : null;

    private ProbeResult Probe(string? cachedCapabilities)
    {
        string? capsString = string.IsNullOrEmpty(cachedCapabilities) ? _device.ReadCapabilities() : cachedCapabilities;
        var caps = CapabilitiesParser.Parse(capsString);
        bool hasCaps = caps.Vcp.Count > 0;

        var codes = VcpCatalog.ExtraSliders.Select(s => s.Code)
            .Concat(new[] { VcpCatalog.InputSource, VcpCatalog.DisplayMode, VcpCatalog.ColorPreset, VcpCatalog.Mute, VcpCatalog.PowerMode });

        var values = new Dictionary<byte, (uint, uint)>();
        foreach (byte code in codes)
        {
            // Trust the capabilities string when there is one; otherwise ask for every code.
            if (hasCaps && !caps.Supports(code))
                continue;
            if (Read(code) is { } value)
                values[code] = value;
        }
        return new ProbeResult(capsString, caps, values);
    }

    private void SetPower(byte mode) => Task.Run(() => _device.TrySetVcp(VcpCatalog.PowerMode, mode));

    private async Task ResetAsync(byte code)
    {
        await Task.Run(() => _device.TrySetVcp(code, 1));
        await Task.Delay(1500); // let the monitor settle before reading back
        await RefreshValuesAsync();
    }

    private async Task RawReadAsync()
    {
        if (!TryParseNumber(RawCode, hexByDefault: true, out uint code) || code > 0xFF)
        {
            RawResult = "Code must be a hex byte, e.g. 60.";
            return;
        }
        var value = await Task.Run(() => Read((byte)code));
        RawResult = value is var (current, maximum)
            ? $"0x{code:X2}: current {current} (0x{current:X}), maximum {maximum} (0x{maximum:X})"
            : $"0x{code:X2}: no reply (unsupported or write-only).";
    }

    private async Task RawWriteAsync()
    {
        if (!TryParseNumber(RawCode, hexByDefault: true, out uint code) || code > 0xFF)
        {
            RawResult = "Code must be a hex byte, e.g. 60.";
            return;
        }
        if (!TryParseNumber(RawValue, hexByDefault: false, out uint value))
        {
            RawResult = "Value must be a number: decimal (17) or hex (0x11).";
            return;
        }
        bool ok = await Task.Run(() => _device.TrySetVcp((byte)code, value));
        RawResult = ok ? $"0x{code:X2} set to {value} (0x{value:X})." : $"0x{code:X2}: the monitor rejected the write.";
    }

    private static bool TryParseNumber(string text, bool hexByDefault, out uint value)
    {
        text = text.Trim();
        bool hex = hexByDefault;
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            text = text[2..];
            hex = true;
        }
        return uint.TryParse(text, hex ? NumberStyles.HexNumber : NumberStyles.None, CultureInfo.InvariantCulture, out value);
    }
}
