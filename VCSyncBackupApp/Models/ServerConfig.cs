using VCSyncBackupApp.Infrastructure;

namespace VCSyncBackupApp.Models;

public sealed class ServerConfig : ObservableObject
{
    private string _name = "NewServer";
    private string _ipAddress = string.Empty;
    private string _remoteDataPath = "/opt/outline/persisted-state";
    private string _remoteConfigPath = "/opt/outline/access.txt";
    private int _progressPercent;
    private string _status = "Idle";
    private string _progressDetail = "0 B / 0 B";

    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    public string IpAddress
    {
        get => _ipAddress;
        set => SetProperty(ref _ipAddress, value);
    }

    public string RemoteDataPath
    {
        get => _remoteDataPath;
        set => SetProperty(ref _remoteDataPath, value);
    }

    public string RemoteConfigPath
    {
        get => _remoteConfigPath;
        set => SetProperty(ref _remoteConfigPath, value);
    }

    public int ProgressPercent
    {
        get => _progressPercent;
        set => SetProperty(ref _progressPercent, value);
    }

    public string Status
    {
        get => _status;
        set => SetProperty(ref _status, value);
    }

    public string ProgressDetail
    {
        get => _progressDetail;
        set => SetProperty(ref _progressDetail, value);
    }
}
