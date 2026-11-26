using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Eigen.Models;

namespace Eigen.Services;

public sealed class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public string SettingsFilePath { get; }

    public SettingsService(string? customPath = null)
    {
        if (!string.IsNullOrWhiteSpace(customPath))
        {
            SettingsFilePath = customPath;
        }
        else
        {
            var appData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Eigen");
            SettingsFilePath = Path.Combine(appData, "settings.json");
        }
    }

    public AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsFilePath))
            {
                var json = File.ReadAllText(SettingsFilePath);
                var loaded = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
                if (loaded is not null)
                {
                    MigrateLegacyVerticalGap(json, loaded);
                    return loaded;
                }
            }
        }
        catch
        {
            // Fall back to defaults on corrupt file
        }

        return CreateDefaultSettings();
    }

    public void Save(AppSettings settings)
    {
        try
        {
            var dir = Path.GetDirectoryName(SettingsFilePath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            var json = JsonSerializer.Serialize(settings, JsonOptions);
            File.WriteAllText(SettingsFilePath, json);
        }
        catch
        {
            // Ignore transient IO failures on exit
        }
    }

    private static void MigrateLegacyVerticalGap(string json, AppSettings settings)
    {
        if (settings.ItemVerticalGap is not double legacyGap)
            return;

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (!root.TryGetProperty(nameof(AppSettings.DetailsVerticalGap), out _))
            settings.DetailsVerticalGap = legacyGap;
        if (!root.TryGetProperty(nameof(AppSettings.TilesVerticalGap), out _))
            settings.TilesVerticalGap = legacyGap + 4.0;
        if (!root.TryGetProperty(nameof(AppSettings.CompactVerticalGap), out _))
            settings.CompactVerticalGap = legacyGap;
        if (!root.TryGetProperty(nameof(AppSettings.SidebarVerticalGap), out _))
            settings.SidebarVerticalGap = legacyGap;

        settings.ItemVerticalGap = null;
    }

    private static AppSettings CreateDefaultSettings()
    {
        var pinned = new List<string>();
        AddIfExists(pinned, Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
        AddIfExists(pinned, NativeShellService.GetKnownDownloadsPath());
        AddIfExists(pinned, Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
        AddIfExists(pinned, Environment.GetFolderPath(Environment.SpecialFolder.MyPictures));

        return new AppSettings
        {
            PinnedFolders = pinned
        };
    }

    private static void AddIfExists(List<string> list, string path)
    {
        if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path) &&
            !list.Contains(path, StringComparer.OrdinalIgnoreCase))
        {
            list.Add(path);
        }
    }
}
