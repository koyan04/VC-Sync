using System.Text.Json;
using System.IO;
using VCSyncBackupApp.Models;

namespace VCSyncBackupApp.Services;

public sealed class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly string _configPath;

    public SettingsService(string appDataPath)
    {
        _configPath = Path.Combine(appDataPath, "config.json");
    }

    public async Task<AppConfig> LoadAsync()
    {
        if (!File.Exists(_configPath))
        {
            return new AppConfig();
        }

        await using var stream = File.OpenRead(_configPath);
        return await JsonSerializer.DeserializeAsync<AppConfig>(stream, JsonOptions) ?? new AppConfig();
    }

    public async Task SaveAsync(AppConfig config)
    {
        var directory = Path.GetDirectoryName(_configPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using var stream = File.Create(_configPath);
        await JsonSerializer.SerializeAsync(stream, config, JsonOptions);
    }

    public string GetConfigPath() => _configPath;
}
