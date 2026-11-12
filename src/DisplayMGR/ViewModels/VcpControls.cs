using System.Collections.ObjectModel;
using System.Windows.Input;
using DisplayMGR.Monitors;

namespace DisplayMGR.ViewModels;

/// <summary>
/// A continuous VCP value. DDC/CI writes take ~50 ms, so while a slider is dragged
/// only the latest value is sent: intermediate ones are dropped.
/// </summary>
public sealed class VcpSliderViewModel : ObservableObject
{
    private readonly IMonitorDevice _device;
    private readonly object _gate = new();
    private double _value;
    private uint _pending;
    private uint _written;
    private bool _writing;

    public VcpSliderViewModel(IMonitorDevice device, string name, byte code, uint current, uint maximum)
    {
        _device = device;
        Name = name;
        Code = code;
        Maximum = maximum == 0 ? 100 : maximum;
        _value = Math.Min(current, Maximum);
        _pending = _written = (uint)_value;
    }

    public string Name { get; }
    public byte Code { get; }
    public double Maximum { get; }

    /// <summary>Raised when the user (not a refresh or unison) changes the value.</summary>
    public event Action<VcpSliderViewModel>? UserChanged;

    public double Value
    {
        get => _value;
        set
        {
            if (Apply(value, write: true))
                UserChanged?.Invoke(this);
        }
    }

    public double Fraction => _value / Maximum;

    /// <summary>Shows a value read back from the monitor without writing it again.</summary>
    public void ShowDeviceValue(uint current) => Apply(current, write: false);

    /// <summary>Sets the value as a 0..1 fraction of the range, without raising <see cref="UserChanged"/>.</summary>
    public void SetFraction(double fraction) => Apply(fraction * Maximum, write: true);

    private bool Apply(double value, bool write)
    {
        double rounded = Math.Clamp(Math.Round(value), 0, Maximum);
        if (rounded == _value)
            return false;
        _value = rounded;
        OnPropertyChanged(nameof(Value));

        lock (_gate)
        {
            _pending = (uint)rounded;
            if (!write)
            {
                _written = _pending;
            }
            else if (!_writing)
            {
                _writing = true;
                Task.Run(WriteLoop);
            }
        }
        return true;
    }

    private void WriteLoop()
    {
        while (true)
        {
            uint target;
            lock (_gate)
            {
                if (_pending == _written)
                {
                    _writing = false;
                    return;
                }
                target = _pending;
            }
            _device.TrySetVcp(Code, target);
            lock (_gate)
                _written = target;
        }
    }
}

public sealed class VcpOption : ObservableObject
{
    private bool _isSelected;

    public VcpOption(byte value, string label)
    {
        Value = value;
        Label = label;
    }

    public byte Value { get; }
    public string Label { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set => Set(ref _isSelected, value);
    }
}

/// <summary>A VCP code with a fixed list of values: input source, picture mode, colour preset.</summary>
public sealed class VcpOptionGroupViewModel : ObservableObject
{
    private readonly IMonitorDevice _device;

    public VcpOptionGroupViewModel(IMonitorDevice device, string title, byte code,
        IEnumerable<byte> values, Func<byte, string> nameOf, uint? current)
    {
        _device = device;
        Title = title;
        Code = code;
        foreach (byte value in values)
            Options.Add(new VcpOption(value, nameOf(value)));
        ShowDeviceValue(current);
        SelectCommand = new RelayCommand(parameter =>
        {
            if (parameter is VcpOption option)
                _ = SelectAsync(option);
        });
    }

    public string Title { get; }
    public byte Code { get; }
    public ObservableCollection<VcpOption> Options { get; } = new();
    public ICommand SelectCommand { get; }

    public void ShowDeviceValue(uint? current)
    {
        // Monitors may put extra data in the high byte; the selection is the low byte.
        foreach (var option in Options)
            option.IsSelected = current is uint value && option.Value == (byte)(value & 0xFF);
    }

    private async Task SelectAsync(VcpOption option)
    {
        bool ok = await Task.Run(() => _device.TrySetVcp(Code, option.Value));
        if (ok)
        {
            foreach (var other in Options)
                other.IsSelected = ReferenceEquals(other, option);
        }
    }
}
