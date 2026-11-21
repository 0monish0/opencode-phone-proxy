using System.Text.Json;
using System.Text.Json.Serialization;
using OpenCodePhoneProxy.Models;

namespace OpenCodePhoneProxy.Services;

public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public SettingsStore(string? dataDirectory = null)
    {
        DataDirectory = dataDirectory ??
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "OpenCodePhoneProxy");
    }

    public string DataDirectory { get; }

    public string SettingsPath => Path.Combine(DataDirectory, "settings.json");

    public string? LastLoadError { get; private set; }

    public AppSettings Load()
    {
        LastLoadError = null;
        try
        {
            if (!File.Exists(SettingsPath))
            {
                return new AppSettings();
            }

            var json = File.ReadAllText(SettingsPath);
            var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
            if (settings is null)
            {
                throw new JsonException("Settings file was empty.");
            }

            return settings;
        }
        catch (Exception ex)
        {
            LastLoadError = ex.Message;
            BackupCorrupt();
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        Directory.CreateDirectory(DataDirectory);
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        var temp = SettingsPath + ".tmp";
        File.WriteAllText(temp, json);
        File.Move(temp, SettingsPath, overwrite: true);
    }

    public string? BackupCorrupt()
    {
        try
        {
            if (!File.Exists(SettingsPath))
            {
                return null;
            }

            var backup = Path.Combine(
                DataDirectory,
                $"settings.bad.{DateTimeOffset.Now:yyyyMMdd-HHmmss}.json");
            File.Move(SettingsPath, backup, overwrite: true);
            return backup;
        }
        catch
        {
            return null;
        }
    }
}
