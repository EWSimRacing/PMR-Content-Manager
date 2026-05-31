using System.IO;
using System.Text.Json;
using EWSR_PMR_ModApp.Core.Common;

namespace EWSR_PMR_ModApp.UI.Infrastructure;

/// <summary>
/// Persists lightweight UI-layer settings (e.g., user-configured game path) to
/// %APPDATA%\EWSR_PMR_ModApp\ui-settings.json.
/// </summary>
public sealed class UISettingsStore
{
    private static readonly string SettingsPath =
        Path.Combine(AppPaths.AppDataRoot, "ui-settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public UISettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return new UISettings();
            string json = File.ReadAllText(SettingsPath);
            return JsonSerializer.Deserialize<UISettings>(json, JsonOptions) ?? new UISettings();
        }
        catch
        {
            return new UISettings();
        }
    }

    public void Save(UISettings settings)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings, JsonOptions));
        }
        catch
        {
            // Settings persistence failure is non-fatal.
        }
    }
}

public sealed class UISettings
{
    /// <summary>User-manually-configured game data path override (null = auto-detect).</summary>
    public string? UserConfiguredGamePath { get; set; }
}
