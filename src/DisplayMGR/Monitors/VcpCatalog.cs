namespace DisplayMGR.Monitors;

/// <summary>VCP codes from the VESA MCCS standard and the names of their values.</summary>
public static class VcpCatalog
{
    public const byte FactoryReset = 0x04;
    public const byte ColorReset = 0x08;
    public const byte Brightness = 0x10;
    public const byte Contrast = 0x12;
    public const byte ColorPreset = 0x14;
    public const byte RedGain = 0x16;
    public const byte GreenGain = 0x18;
    public const byte BlueGain = 0x1A;
    public const byte InputSource = 0x60;
    public const byte Volume = 0x62;
    public const byte Sharpness = 0x87;
    public const byte Mute = 0x8D;
    public const byte PowerMode = 0xD6;
    public const byte DisplayMode = 0xDC;

    public const byte MuteOn = 0x01;
    public const byte MuteOff = 0x02;

    public const byte PowerOn = 0x01;
    public const byte PowerStandby = 0x02;
    public const byte PowerOff = 0x04;
    public const byte PowerOffHard = 0x05;

    /// <summary>Sliders shown under "More", in display order.</summary>
    public static readonly IReadOnlyList<(byte Code, string Name)> ExtraSliders = new[]
    {
        (Contrast, "Contrast"),
        (Volume, "Volume"),
        (Sharpness, "Sharpness"),
        (RedGain, "Red gain"),
        (GreenGain, "Green gain"),
        (BlueGain, "Blue gain"),
    };

    /// <summary>Inputs offered when a monitor answers DDC/CI but will not list its inputs.</summary>
    public static readonly IReadOnlyList<byte> FallbackInputs = new byte[] { 0x0F, 0x10, 0x11, 0x12, 0x1B };

    private static readonly Dictionary<byte, string> Inputs = new()
    {
        [0x01] = "VGA 1", [0x02] = "VGA 2", [0x03] = "DVI 1", [0x04] = "DVI 2",
        [0x05] = "Composite 1", [0x06] = "Composite 2", [0x07] = "S-Video 1", [0x08] = "S-Video 2",
        [0x09] = "Tuner 1", [0x0A] = "Tuner 2", [0x0B] = "Tuner 3",
        [0x0C] = "Component 1", [0x0D] = "Component 2", [0x0E] = "Component 3",
        [0x0F] = "DisplayPort 1", [0x10] = "DisplayPort 2", [0x11] = "HDMI 1", [0x12] = "HDMI 2",
        [0x1B] = "USB-C",
    };

    private static readonly Dictionary<byte, string> ColorPresets = new()
    {
        [0x01] = "sRGB", [0x02] = "Native", [0x03] = "4000 K", [0x04] = "5000 K", [0x05] = "6500 K",
        [0x06] = "7500 K", [0x07] = "8200 K", [0x08] = "9300 K", [0x09] = "10000 K", [0x0A] = "11500 K",
        [0x0B] = "User 1", [0x0C] = "User 2", [0x0D] = "User 3",
    };

    private static readonly Dictionary<byte, string> DisplayModes = new()
    {
        [0x00] = "Standard", [0x01] = "Productivity", [0x02] = "Mixed", [0x03] = "Movie", [0x04] = "User",
        [0x05] = "Games", [0x06] = "Sports", [0x07] = "Professional", [0x08] = "Standard (eco)",
        [0x09] = "Standard (low power)", [0x0A] = "Demo", [0xF0] = "Dynamic contrast",
    };

    public static string InputName(byte value) =>
        Inputs.TryGetValue(value, out var name) ? name : $"Input 0x{value:X2}";

    public static string ColorPresetName(byte value) =>
        ColorPresets.TryGetValue(value, out var name) ? name : $"Preset 0x{value:X2}";

    public static string DisplayModeName(byte value) =>
        DisplayModes.TryGetValue(value, out var name) ? name : $"Mode 0x{value:X2}";
}
