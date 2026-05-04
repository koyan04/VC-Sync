using System.IO;
using System.Text.Json;

namespace VCSyncBackupApp.Models;

public sealed class ApiKeyFileEntry
{
    public string FileName { get; init; } = string.Empty;
    public string FilePath { get; init; } = string.Empty;
    public string ServerName { get; init; } = string.Empty;
    public string IpAddress { get; init; } = string.Empty;
    public DateTime CreatedDate { get; init; }
    public string ApiKeyJson { get; init; } = string.Empty;

    public static bool TryLoad(string filePath, out ApiKeyFileEntry? entry)
    {
        entry = null;

        if (!File.Exists(filePath))
        {
            return false;
        }

        try
        {
            var apiKeyJson = File.ReadAllText(filePath);
            var parsed = ParseApiKeyJson(apiKeyJson);
            var fileName = Path.GetFileName(filePath);

            entry = new ApiKeyFileEntry
            {
                FileName = fileName,
                FilePath = filePath,
                ServerName = ExtractServerName(fileName),
                IpAddress = parsed.TryGetValue("apiUrl", out var apiUrl)
                    ? ExtractIpAddress(apiUrl)
                    : string.Empty,
                CreatedDate = File.GetCreationTime(filePath),
                ApiKeyJson = apiKeyJson
            };

            return true;
        }
        catch
        {
            return false;
        }
    }

    private static Dictionary<string, string> ParseApiKeyJson(string content)
    {
        using var document = JsonDocument.Parse(content);
        return document.RootElement.EnumerateObject()
            .ToDictionary(property => property.Name, property => property.Value.GetString() ?? string.Empty, StringComparer.OrdinalIgnoreCase);
    }

    private static string ExtractServerName(string fileName)
    {
        const string suffix = "-access.txt";

        if (fileName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
        {
            return fileName[..^suffix.Length];
        }

        return Path.GetFileNameWithoutExtension(fileName);
    }

    private static string ExtractIpAddress(string apiUrl)
    {
        if (Uri.TryCreate(apiUrl, UriKind.Absolute, out var uri))
        {
            return uri.Host;
        }

        return string.Empty;
    }
}