namespace VCSyncBackupApp.Models;

public sealed class AppConfig
{
    public string WinScpAssemblyPath { get; set; } = string.Empty;
    public string PrivateKeyPath { get; set; } = string.Empty;
    public string BaseBackupDirectory { get; set; } = string.Empty;
    public int RetentionCount { get; set; } = 1;
    public bool BackupConfigOnly { get; set; }
    public bool RestoreConfigOnly { get; set; }
    public List<ServerConfig> Servers { get; set; } = new();
}
