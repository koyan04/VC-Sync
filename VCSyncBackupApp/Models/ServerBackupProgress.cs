namespace VCSyncBackupApp.Models;

public sealed class ServerBackupProgress
{
    public required string ServerName { get; init; }
    public required string Status { get; init; }
    public int ProgressPercent { get; init; }
    public string? Message { get; init; }
    public long? TransferredBytes { get; init; }
    public long? TotalBytes { get; init; }
}
