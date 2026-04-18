namespace VCSyncBackupApp.Models;

public sealed class RestoreRequest
{
    public string ServerIpAddress { get; set; } = string.Empty;
    public string DataZipFilePath { get; set; } = string.Empty;
    public string ConfigFilePath { get; set; } = string.Empty;
    public string DestinationPath { get; set; } = "/root/shadowbox/persisted-state/";
    public bool ConfigOnly { get; set; }
    public string WinScpAssemblyPath { get; set; } = string.Empty;
    public string PrivateKeyPath { get; set; } = string.Empty;
    public string Passphrase { get; set; } = string.Empty;
}
