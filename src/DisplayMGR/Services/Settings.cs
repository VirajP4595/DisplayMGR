using System.IO;
using System.Text.Json;
using Microsoft.Win32;

namespace DisplayMGR.Services;

public sealed class AppSettings
{
    public bool Unison { get; set; }
    public bool KeepPanelOpen { get; set; }
    public bool WidgetVisible { get; set; } = true;
    public bool WidgetTopmost { get; set; }
    public bool WidgetShowInputs { get; set; } = true;
    public double? WidgetLeft { get; set; }
    public double? WidgetTop { get; set; }

    /// <summary>Capabilities strings by monitor id. Reading one takes seconds, so they are kept.</summary>
    public Dictionary<string, string> CapabilitiesCache { get; set; } = new();
}

public static class SettingsStore
{
    private static readonly string Folder =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DisplayMGR");

    private static readonly string FilePath = Path.Combine(Folder, "settings.json");

    public static string LogPath => Path.Combine(Folder, "error.log");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
        }
        catch (Exception)
        {
            // A corrupt settings file must not stop the app from starting.
        }
        return new AppSettings();
    }

    public static void Save(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(Folder);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception)
        {
        }
    }

    public static void Log(Exception exception)
    {
        try
        {
            Directory.CreateDirectory(Folder);
            File.AppendAllText(LogPath, $"{DateTime.Now:u} {exception}{Environment.NewLine}");
        }
        catch (Exception)
        {
        }
    }
}

/// <summary>"Start with Windows", stored in the current user's Run key.</summary>
public static class StartupRegistration
{
    public const string TrayArgument = "--tray";

    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "DisplayMGR";

    public static bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is not null;
        }
    }

    public static void Set(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (enabled && Environment.ProcessPath is string path)
                key.SetValue(ValueName, $"\"{path}\" {TrayArgument}");
            else
                key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
        catch (Exception exception)
        {
            SettingsStore.Log(exception);
        }
    }
}
