using System.Runtime.InteropServices;
using DisplayMGR.Native;

namespace DisplayMGR.Monitors;

public sealed record DetectedMonitor(string Id, string Name, bool IsInternal, IMonitorDevice Device);

/// <summary>Finds the connected monitors and picks DDC/CI or WMI for each.</summary>
public static class MonitorEnumerator
{
    private sealed record TargetInfo(string FriendlyName, string DevicePath, bool IsInternal);

    public static List<DetectedMonitor> Enumerate()
    {
        var targetsByGdiName = QueryTargets();
        bool wmiFree = WmiMonitor.IsAvailable();
        var monitors = new List<DetectedMonitor>();

        foreach (var (hMonitor, gdiName) in EnumerateDisplays())
        {
            if (!NativeMethods.GetNumberOfPhysicalMonitorsFromHMONITOR(hMonitor, out uint count) || count == 0)
                continue;
            var physical = new NativeMethods.PHYSICAL_MONITOR[count];
            if (!NativeMethods.GetPhysicalMonitorsFromHMONITOR(hMonitor, count, physical))
                continue;

            targetsByGdiName.TryGetValue(gdiName, out var targets);

            for (int i = 0; i < physical.Length; i++)
            {
                TargetInfo? target = targets is not null && i < targets.Count ? targets[i] : null;
                IntPtr handle = physical[i].hPhysicalMonitor;

                // Without display-config data, fall back to "does it answer DDC/CI?".
                bool useWmi = wmiFree && (target?.IsInternal ??
                    !NativeMethods.GetVCPFeatureAndVCPFeatureReply(handle, VcpCatalog.Brightness, out _, out _, out _));

                IMonitorDevice device;
                if (useWmi)
                {
                    NativeMethods.DestroyPhysicalMonitor(handle);
                    device = new WmiMonitor();
                    wmiFree = false; // WMI brightness addresses one panel
                }
                else
                {
                    device = new DdcMonitor(handle);
                }

                string name = target?.FriendlyName ?? "";
                if (name.Length == 0)
                    name = useWmi ? "Built-in display" : physical[i].szPhysicalMonitorDescription;
                if (string.IsNullOrWhiteSpace(name))
                    name = "Monitor";

                string id = !string.IsNullOrEmpty(target?.DevicePath) ? target.DevicePath : $"{gdiName}#{i}";
                monitors.Add(new DetectedMonitor(id, name, useWmi, device));
            }
        }

        return NumberDuplicates(monitors);
    }

    private static List<DetectedMonitor> NumberDuplicates(List<DetectedMonitor> monitors)
    {
        var result = new List<DetectedMonitor>(monitors.Count);
        var seen = new Dictionary<string, int>();
        foreach (var monitor in monitors)
        {
            bool duplicated = monitors.Count(m => m.Name == monitor.Name) > 1;
            int n = seen[monitor.Name] = seen.GetValueOrDefault(monitor.Name) + 1;
            result.Add(duplicated ? monitor with { Name = $"{monitor.Name} ({n})" } : monitor);
        }
        return result;
    }

    private static List<(IntPtr Handle, string GdiName)> EnumerateDisplays()
    {
        var displays = new List<(IntPtr, string)>();
        NativeMethods.MonitorEnumProc callback = (hMonitor, _, _, _) =>
        {
            var info = new NativeMethods.MONITORINFOEX { cbSize = Marshal.SizeOf<NativeMethods.MONITORINFOEX>() };
            displays.Add((hMonitor, NativeMethods.GetMonitorInfo(hMonitor, ref info) ? info.szDevice : ""));
            return true;
        };
        NativeMethods.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero);
        GC.KeepAlive(callback);
        return displays;
    }

    /// <summary>Maps a GDI device name (\\.\DISPLAY1) to the monitors attached to it.</summary>
    private static Dictionary<string, List<TargetInfo>> QueryTargets()
    {
        var map = new Dictionary<string, List<TargetInfo>>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (NativeMethods.GetDisplayConfigBufferSizes(NativeMethods.QDC_ONLY_ACTIVE_PATHS, out uint pathCount, out uint modeCount) != 0)
                return map;

            var paths = new NativeMethods.DISPLAYCONFIG_PATH_INFO[pathCount];
            var modes = new NativeMethods.DISPLAYCONFIG_MODE_INFO[modeCount];
            if (NativeMethods.QueryDisplayConfig(NativeMethods.QDC_ONLY_ACTIVE_PATHS, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero) != 0)
                return map;

            for (int i = 0; i < pathCount; i++)
            {
                var source = new NativeMethods.DISPLAYCONFIG_SOURCE_DEVICE_NAME();
                source.header.type = NativeMethods.DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME;
                source.header.size = (uint)Marshal.SizeOf<NativeMethods.DISPLAYCONFIG_SOURCE_DEVICE_NAME>();
                source.header.adapterId = paths[i].sourceInfo.adapterId;
                source.header.id = paths[i].sourceInfo.id;
                if (NativeMethods.DisplayConfigGetDeviceInfo(ref source) != 0)
                    continue;

                var target = new NativeMethods.DISPLAYCONFIG_TARGET_DEVICE_NAME();
                target.header.type = NativeMethods.DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME;
                target.header.size = (uint)Marshal.SizeOf<NativeMethods.DISPLAYCONFIG_TARGET_DEVICE_NAME>();
                target.header.adapterId = paths[i].targetInfo.adapterId;
                target.header.id = paths[i].targetInfo.id;
                if (NativeMethods.DisplayConfigGetDeviceInfo(ref target) != 0)
                    continue;

                if (!map.TryGetValue(source.viewGdiDeviceName, out var list))
                    map[source.viewGdiDeviceName] = list = new List<TargetInfo>();
                list.Add(new TargetInfo(
                    target.monitorFriendlyDeviceName ?? "",
                    target.monitorDevicePath ?? "",
                    IsInternalTechnology(target.outputTechnology)));
            }
        }
        catch (Exception)
        {
            // Names are a nicety; enumeration still works without them.
        }
        return map;
    }

    private static bool IsInternalTechnology(uint outputTechnology) => outputTechnology switch
    {
        0x80000000 => true, // INTERNAL
        6 => true,          // LVDS
        11 => true,         // DISPLAYPORT_EMBEDDED
        13 => true,         // UDI_EMBEDDED
        _ => false,
    };
}
