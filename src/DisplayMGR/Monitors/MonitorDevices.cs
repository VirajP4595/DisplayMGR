using System.Management;
using System.Text;
using DisplayMGR.Native;

namespace DisplayMGR.Monitors;

/// <summary>A monitor that can be read and written with VCP codes. All calls are blocking and slow.</summary>
public interface IMonitorDevice : IDisposable
{
    /// <summary>True for DDC/CI monitors; false for panels that only expose brightness.</summary>
    bool SupportsVcp { get; }

    bool TryGetVcp(byte code, out uint current, out uint maximum);

    bool TrySetVcp(byte code, uint value);

    /// <summary>The raw capabilities string, or null when the monitor does not provide one.</summary>
    string? ReadCapabilities();
}

/// <summary>An external monitor driven over DDC/CI.</summary>
public sealed class DdcMonitor : IMonitorDevice
{
    private const int Attempts = 3;
    private const int RetryDelayMs = 50;

    private readonly object _gate = new();
    private IntPtr _handle;

    public DdcMonitor(IntPtr physicalMonitorHandle) => _handle = physicalMonitorHandle;

    public bool SupportsVcp => true;

    public bool TryGetVcp(byte code, out uint current, out uint maximum)
    {
        current = 0;
        maximum = 0;
        lock (_gate)
        {
            for (int attempt = 0; attempt < Attempts && _handle != IntPtr.Zero; attempt++)
            {
                if (NativeMethods.GetVCPFeatureAndVCPFeatureReply(_handle, code, out _, out current, out maximum))
                    return true;
                Thread.Sleep(RetryDelayMs);
            }
        }
        return false;
    }

    public bool TrySetVcp(byte code, uint value)
    {
        lock (_gate)
        {
            for (int attempt = 0; attempt < Attempts && _handle != IntPtr.Zero; attempt++)
            {
                if (NativeMethods.SetVCPFeature(_handle, code, value))
                    return true;
                Thread.Sleep(RetryDelayMs);
            }
        }
        return false;
    }

    public string? ReadCapabilities()
    {
        lock (_gate)
        {
            for (int attempt = 0; attempt < 2 && _handle != IntPtr.Zero; attempt++)
            {
                if (NativeMethods.GetCapabilitiesStringLength(_handle, out uint length) && length > 1)
                {
                    var buffer = new byte[length];
                    if (NativeMethods.CapabilitiesRequestAndCapabilitiesReply(_handle, buffer, length))
                    {
                        string text = Encoding.ASCII.GetString(buffer).TrimEnd('\0').Trim();
                        if (text.Length > 0)
                            return text;
                    }
                }
                Thread.Sleep(RetryDelayMs);
            }
        }
        return null;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_handle != IntPtr.Zero)
            {
                NativeMethods.DestroyPhysicalMonitor(_handle);
                _handle = IntPtr.Zero;
            }
        }
    }
}

/// <summary>A built-in laptop panel. Windows only exposes its brightness, through WMI.</summary>
public sealed class WmiMonitor : IMonitorDevice
{
    private const string Scope = @"root\wmi";

    public bool SupportsVcp => false;

    /// <summary>True when this PC has a panel whose brightness WMI can control.</summary>
    public static bool IsAvailable() => ReadBrightness() is not null;

    public bool TryGetVcp(byte code, out uint current, out uint maximum)
    {
        current = 0;
        maximum = 100;
        if (code != VcpCatalog.Brightness || ReadBrightness() is not byte value)
            return false;
        current = value;
        return true;
    }

    public bool TrySetVcp(byte code, uint value)
    {
        if (code != VcpCatalog.Brightness)
            return false;
        try
        {
            using var searcher = new ManagementObjectSearcher(Scope, "SELECT * FROM WmiMonitorBrightnessMethods");
            using var results = searcher.Get();
            foreach (ManagementObject instance in results)
            {
                using (instance)
                {
                    instance.InvokeMethod("WmiSetBrightness", new object[] { (uint)1, (byte)Math.Min(value, 100) });
                    return true;
                }
            }
        }
        catch (Exception)
        {
            // WMI reports "not supported" on desktops and some drivers.
        }
        return false;
    }

    public string? ReadCapabilities() => null;

    public void Dispose()
    {
    }

    private static byte? ReadBrightness()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(Scope, "SELECT CurrentBrightness FROM WmiMonitorBrightness");
            using var results = searcher.Get();
            foreach (ManagementObject instance in results)
            {
                using (instance)
                    return (byte)instance["CurrentBrightness"];
            }
        }
        catch (Exception)
        {
        }
        return null;
    }
}
