using System.Collections.ObjectModel;
using System.IO;
using System.Media;
using System.Text;
using System.Text.Json;
using System.Windows;
using WpfMessageBox = System.Windows.MessageBox;
using WpfMessageBoxButton = System.Windows.MessageBoxButton;
using WpfMessageBoxImage = System.Windows.MessageBoxImage;
using WpfOpenFileDialog = Microsoft.Win32.OpenFileDialog;
using WpfSaveFileDialog = Microsoft.Win32.SaveFileDialog;
using VCSyncBackupApp.Infrastructure;
using VCSyncBackupApp.Models;
using VCSyncBackupApp.Services;

namespace VCSyncBackupApp.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private const string RootRestorePath = "/root/shadowbox/persisted-state/";
    private const string OutlineRestorePath = "/opt/outline/persisted-state/";
    private const string ApiKeyRootSourcePath = "/root/shadowbox/";
    private const string ApiKeyOutlineSourcePath = "/opt/outline/";
    private const string ServerConfigFileName = "shadowbox_server_config.json";

    private readonly string _appDataPath;
    private readonly SettingsService _settingsService;
    private readonly SecretStore _secretStore;
    private readonly BackupService _backupService;
    private readonly RestoreService _restoreService;
    private readonly ApiKeyService _apiKeyService;
    private Window? _ownerWindow;

    private ServerConfig? _selectedServer;
    private string _winScpAssemblyPath = string.Empty;
    private string _privateKeyPath = string.Empty;
    private string _passphrase = string.Empty;
    private string _baseBackupDirectory = string.Empty;
    private int _retentionCount = 1;
    private bool _backupConfigOnly;
    private bool _backupServerConfigOnly;
    private string _logText = string.Empty;
    private bool _isBusy;
    private int _overallProgress;
    private CancellationTokenSource? _backupCts;
    private CancellationTokenSource? _restoreCts;
    private string _restoreServerIpAddress = string.Empty;
    private string _restoreDataZipFilePath = string.Empty;
    private string _restoreConfigFilePath = string.Empty;
    private string _restoreServerConfigFilePath = string.Empty;
    private string _restoreDestinationPath = RootRestorePath;
    private bool _isRootRestoreDestinationSelected = true;
    private bool _isOutlineRestoreDestinationSelected;
    private bool _isRestoreDataZipEnabled = true;
    private string _restoreTerminalText = string.Empty;
    private string _restoreCommandPreview = string.Empty;
    private string _restoreSessionStatus = "Idle";
    private bool _isRestoreRunning;
    private bool _isRestoreDryRun = true;
    private bool _restoreConfigOnly;
    private bool _restoreServerConfigOnly;
    private bool _isHomePage = true;
    private bool _isBackupPage;
    private bool _isRestorePage;
    private bool _isApiKeysPage;
    private string _apiKeyServerIpAddress = string.Empty;
    private string _apiKeyServerName = string.Empty;
    private string _apiKeyDestinationFolder = string.Empty;
    private string _apiKeyValue = string.Empty;
    private bool _isApiKeyRootSourceSelected = true;
    private bool _isApiKeyOutlineSourceSelected;

    public MainViewModel()
    {
        _appDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VCSyncBackupApp");
        Directory.CreateDirectory(_appDataPath);

        _settingsService = new SettingsService(_appDataPath);
        _secretStore = new SecretStore(_appDataPath);
        _backupService = new BackupService();
        _restoreService = new RestoreService();
        _apiKeyService = new ApiKeyService();

        Servers = new ObservableCollection<ServerConfig>();
        RestoreDestinationOptions = new ObservableCollection<string>
        {
            RootRestorePath,
            OutlineRestorePath
        };

        AddServerCommand = new RelayCommand(AddServer, () => !IsBusy);
        DeleteServerCommand = new RelayCommand(DeleteSelectedServer, () => SelectedServer is not null && !IsBusy);
        DeleteAllServersCommand = new RelayCommand(DeleteAllServers, () => Servers.Count > 0 && !IsBusy);
        SaveSettingsCommand = new AsyncRelayCommand(SaveSettingsAsync, () => !IsBusy);
        StartBackupCommand = new AsyncRelayCommand(StartBackupAsync, () => Servers.Count > 0 && !IsBusy);
        StartSelectedBackupCommand = new AsyncRelayCommand(StartSelectedBackupAsync, () => SelectedServer is not null && !IsBusy);
        StopBackupCommand = new RelayCommand(StopBackup, () => IsBusy);
        ImportServersCommand = new AsyncRelayCommand(ImportServersAsync, () => !IsBusy);
        ExportServersCommand = new AsyncRelayCommand(ExportServersAsync, () => Servers.Count > 0 && !IsBusy);
        ImportConfigCommand = new AsyncRelayCommand(ImportConfigurationAsync, () => !IsBusy);
        ExportConfigCommand = new AsyncRelayCommand(ExportConfigurationAsync, () => !IsBusy);
        BrowseAssemblyCommand = new RelayCommand(BrowseAssembly, () => !IsBusy);
        BrowseKeyCommand = new RelayCommand(BrowsePrivateKey, () => !IsBusy);
        BrowseBaseDirectoryCommand = new RelayCommand(BrowseBaseDirectory, () => !IsBusy);
        BrowseRestoreDataZipCommand = new RelayCommand(BrowseRestoreDataZip, () => !IsBusy);
        BrowseRestoreConfigCommand = new RelayCommand(BrowseRestoreConfig, () => !IsBusy);
        BrowseRestoreServerConfigCommand = new RelayCommand(BrowseRestoreServerConfig, () => !IsBusy);
        ClearPasteRestoreServerIpCommand = new RelayCommand(ClearAndPasteRestoreServerIp, () => !IsBusy);
        RefreshRestorePreviewCommand = new RelayCommand(() => RefreshRestorePreview(showHint: true), () => !IsRestoreRunning);
        ApplyServerTuningCommand = new AsyncRelayCommand(StartServerHardeningAsync, () => !IsBusy && !IsRestoreRunning);
        StartRestoreCommand = new AsyncRelayCommand(StartRestoreAsync, () => !IsBusy && !IsRestoreRunning);
        AbortRestoreCommand = new RelayCommand(AbortRestore, () => IsRestoreRunning);
        EndRestoreSessionCommand = new RelayCommand(EndRestoreSession, () => !IsRestoreRunning && !string.IsNullOrWhiteSpace(RestoreTerminalText));
        NavigateHomeCommand = new RelayCommand(NavigateHome);
        NavigateBackupPageCommand = new RelayCommand(NavigateBackupPage);
        NavigateRestorePageCommand = new RelayCommand(NavigateRestorePage);
        NavigateApiKeysPageCommand = new RelayCommand(NavigateApiKeysPage);
        GenerateApiKeyCommand = new AsyncRelayCommand(GenerateApiKeyAsync, () => !IsBusy);
        SaveApiKeyCommand = new AsyncRelayCommand(SaveApiKeyAsync, () => !IsBusy);
        CopyApiKeyCommand = new RelayCommand(CopyApiKeyToClipboard, () => !IsBusy);
        ClearPasteApiKeyServerIpCommand = new RelayCommand(ClearAndPasteApiKeyServerIp, () => !IsBusy);
        BrowseApiKeyDestinationCommand = new RelayCommand(BrowseApiKeyDestinationFolder, () => !IsBusy);
        ViewApiKeyCommand = new RelayCommand(LoadApiKeyEntry, parameter => parameter is ApiKeyFileEntry && !IsBusy);
        DeleteApiKeyCommand = new RelayCommand(DeleteApiKeyEntry, parameter => parameter is ApiKeyFileEntry && !IsBusy);
    }

    public ObservableCollection<ServerConfig> Servers { get; }
    public ObservableCollection<string> RestoreDestinationOptions { get; }

    public ServerConfig? SelectedServer
    {
        get => _selectedServer;
        set
        {
            if (SetProperty(ref _selectedServer, value))
            {
                DeleteServerCommand.RaiseCanExecuteChanged();
                StartSelectedBackupCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string WinScpAssemblyPath
    {
        get => _winScpAssemblyPath;
        set => SetProperty(ref _winScpAssemblyPath, value);
    }

    public string PrivateKeyPath
    {
        get => _privateKeyPath;
        set => SetProperty(ref _privateKeyPath, value);
    }

    public string Passphrase
    {
        get => _passphrase;
        set => SetProperty(ref _passphrase, value);
    }

    public string BaseBackupDirectory
    {
        get => _baseBackupDirectory;
        set => SetProperty(ref _baseBackupDirectory, value);
    }

    public int RetentionCount
    {
        get => _retentionCount;
        set => SetProperty(ref _retentionCount, value < 1 ? 1 : value);
    }

    public bool BackupConfigOnly
    {
        get => _backupConfigOnly;
        set => SetProperty(ref _backupConfigOnly, value);
    }

    public bool BackupServerConfigOnly
    {
        get => _backupServerConfigOnly;
        set => SetProperty(ref _backupServerConfigOnly, value);
    }

    public string LogText
    {
        get => _logText;
        set => SetProperty(ref _logText, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            if (SetProperty(ref _isBusy, value))
            {
                RaiseAllCanExecuteChanges();
            }
        }
    }

    public int OverallProgress
    {
        get => _overallProgress;
        set => SetProperty(ref _overallProgress, Math.Clamp(value, 0, 100));
    }

    public string RestoreServerIpAddress
    {
        get => _restoreServerIpAddress;
        set
        {
            if (SetProperty(ref _restoreServerIpAddress, value))
            {
                RefreshRestorePreview();
            }
        }
    }

    public string RestoreDataZipFilePath
    {
        get => _restoreDataZipFilePath;
        set
        {
            if (SetProperty(ref _restoreDataZipFilePath, value))
            {
                RefreshRestorePreview();
            }
        }
    }

    public string RestoreConfigFilePath
    {
        get => _restoreConfigFilePath;
        set
        {
            if (SetProperty(ref _restoreConfigFilePath, value))
            {
                RefreshRestorePreview();
            }
        }
    }

    public string RestoreServerConfigFilePath
    {
        get => _restoreServerConfigFilePath;
        set
        {
            if (SetProperty(ref _restoreServerConfigFilePath, value))
            {
                RefreshRestorePreview();
            }
        }
    }

    public string RestoreDestinationPath
    {
        get => _restoreDestinationPath;
        set
        {
            if (SetProperty(ref _restoreDestinationPath, value))
            {
                SyncRestoreDestinationSelectionFlags(value);
                RefreshRestorePreview();
            }
        }
    }

    public bool IsRootRestoreDestinationSelected
    {
        get => _isRootRestoreDestinationSelected;
        set
        {
            if (!SetProperty(ref _isRootRestoreDestinationSelected, value) || !value)
            {
                return;
            }

            SetProperty(ref _isOutlineRestoreDestinationSelected, false, nameof(IsOutlineRestoreDestinationSelected));
            RestoreDestinationPath = RootRestorePath;
        }
    }

    public bool IsOutlineRestoreDestinationSelected
    {
        get => _isOutlineRestoreDestinationSelected;
        set
        {
            if (!SetProperty(ref _isOutlineRestoreDestinationSelected, value) || !value)
            {
                return;
            }

            SetProperty(ref _isRootRestoreDestinationSelected, false, nameof(IsRootRestoreDestinationSelected));
            RestoreDestinationPath = OutlineRestorePath;
        }
    }

    public bool IsRestoreDataZipEnabled
    {
        get => _isRestoreDataZipEnabled;
        set => SetProperty(ref _isRestoreDataZipEnabled, value);
    }

    public string RestoreTerminalText
    {
        get => _restoreTerminalText;
        set
        {
            if (SetProperty(ref _restoreTerminalText, value))
            {
                EndRestoreSessionCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string RestoreSessionStatus
    {
        get => _restoreSessionStatus;
        set => SetProperty(ref _restoreSessionStatus, value);
    }

    public string RestoreCommandPreview
    {
        get => _restoreCommandPreview;
        set => SetProperty(ref _restoreCommandPreview, value);
    }

    public bool IsRestoreDryRun
    {
        get => _isRestoreDryRun;
        set
        {
            if (SetProperty(ref _isRestoreDryRun, value))
            {
                RefreshRestorePreview();
            }
        }
    }

    public bool RestoreConfigOnly
    {
        get => _restoreConfigOnly;
        set
        {
            if (SetProperty(ref _restoreConfigOnly, value))
            {
                IsRestoreDataZipEnabled = !(_restoreConfigOnly || _restoreServerConfigOnly);
                RefreshRestorePreview();
            }
        }
    }

    public bool RestoreServerConfigOnly
    {
        get => _restoreServerConfigOnly;
        set
        {
            if (SetProperty(ref _restoreServerConfigOnly, value))
            {
                IsRestoreDataZipEnabled = !(_restoreConfigOnly || _restoreServerConfigOnly);
                RefreshRestorePreview();
            }
        }
    }

    public bool IsRestoreRunning
    {
        get => _isRestoreRunning;
        set
        {
            if (SetProperty(ref _isRestoreRunning, value))
            {
                RaiseAllCanExecuteChanges();
            }
        }
    }

    public bool IsHomePage
    {
        get => _isHomePage;
        set => SetProperty(ref _isHomePage, value);
    }

    public bool IsBackupPage
    {
        get => _isBackupPage;
        set => SetProperty(ref _isBackupPage, value);
    }

    public bool IsRestorePage
    {
        get => _isRestorePage;
        set => SetProperty(ref _isRestorePage, value);
    }

    public bool IsApiKeysPage
    {
        get => _isApiKeysPage;
        set => SetProperty(ref _isApiKeysPage, value);
    }

    public string ApiKeyServerIpAddress
    {
        get => _apiKeyServerIpAddress;
        set => SetProperty(ref _apiKeyServerIpAddress, value);
    }

    public string ApiKeyServerName
    {
        get => _apiKeyServerName;
        set => SetProperty(ref _apiKeyServerName, value);
    }

    public string ApiKeyDestinationFolder
    {
        get => _apiKeyDestinationFolder;
        set
        {
            if (SetProperty(ref _apiKeyDestinationFolder, value))
            {
                RefreshApiKeyFiles();
            }
        }
    }

    public string ApiKeyValue
    {
        get => _apiKeyValue;
        set => SetProperty(ref _apiKeyValue, value);
    }

    public bool IsApiKeyRootSourceSelected
    {
        get => _isApiKeyRootSourceSelected;
        set
        {
            if (!SetProperty(ref _isApiKeyRootSourceSelected, value) || !value)
            {
                return;
            }

            SetProperty(ref _isApiKeyOutlineSourceSelected, false, nameof(IsApiKeyOutlineSourceSelected));
        }
    }

    public bool IsApiKeyOutlineSourceSelected
    {
        get => _isApiKeyOutlineSourceSelected;
        set
        {
            if (!SetProperty(ref _isApiKeyOutlineSourceSelected, value) || !value)
            {
                return;
            }

            SetProperty(ref _isApiKeyRootSourceSelected, false, nameof(IsApiKeyRootSourceSelected));
        }
    }

    public RelayCommand AddServerCommand { get; }
    public RelayCommand DeleteServerCommand { get; }
    public RelayCommand DeleteAllServersCommand { get; }
    public AsyncRelayCommand SaveSettingsCommand { get; }
    public AsyncRelayCommand StartBackupCommand { get; }
    public AsyncRelayCommand StartSelectedBackupCommand { get; }
    public RelayCommand StopBackupCommand { get; }
    public AsyncRelayCommand ImportServersCommand { get; }
    public AsyncRelayCommand ExportServersCommand { get; }
    public AsyncRelayCommand ImportConfigCommand { get; }
    public AsyncRelayCommand ExportConfigCommand { get; }
    public RelayCommand BrowseAssemblyCommand { get; }
    public RelayCommand BrowseKeyCommand { get; }
    public RelayCommand BrowseBaseDirectoryCommand { get; }
    public RelayCommand BrowseRestoreDataZipCommand { get; }
    public RelayCommand BrowseRestoreConfigCommand { get; }
    public RelayCommand BrowseRestoreServerConfigCommand { get; }
    public RelayCommand ClearPasteRestoreServerIpCommand { get; }
    public RelayCommand RefreshRestorePreviewCommand { get; }
    public AsyncRelayCommand ApplyServerTuningCommand { get; }
    public AsyncRelayCommand StartRestoreCommand { get; }
    public RelayCommand AbortRestoreCommand { get; }
    public RelayCommand EndRestoreSessionCommand { get; }
    public RelayCommand NavigateHomeCommand { get; }
    public RelayCommand NavigateBackupPageCommand { get; }
    public RelayCommand NavigateRestorePageCommand { get; }
    public RelayCommand NavigateApiKeysPageCommand { get; }
    public AsyncRelayCommand GenerateApiKeyCommand { get; }
    public AsyncRelayCommand SaveApiKeyCommand { get; }
    public RelayCommand CopyApiKeyCommand { get; }
    public RelayCommand ClearPasteApiKeyServerIpCommand { get; }
    public RelayCommand BrowseApiKeyDestinationCommand { get; }
    public RelayCommand ViewApiKeyCommand { get; }
    public RelayCommand DeleteApiKeyCommand { get; }

    public ObservableCollection<ApiKeyFileEntry> ApiKeyFiles { get; } = new();

    public async Task InitializeAsync()
    {
        var config = await _settingsService.LoadAsync();

        WinScpAssemblyPath = config.WinScpAssemblyPath;
        PrivateKeyPath = config.PrivateKeyPath;
        BaseBackupDirectory = string.IsNullOrWhiteSpace(config.BaseBackupDirectory)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "VChannel", "Premium")
            : config.BaseBackupDirectory;
        RetentionCount = config.RetentionCount <= 0 ? 1 : config.RetentionCount;
        BackupConfigOnly = config.BackupConfigOnly;
        BackupServerConfigOnly = config.BackupServerConfigOnly;
        RestoreConfigOnly = config.RestoreConfigOnly;
        RestoreServerConfigOnly = config.RestoreServerConfigOnly;

        Servers.Clear();
        foreach (var server in config.Servers)
        {
            server.Status = "Idle";
            server.ProgressPercent = 0;
            server.ProgressDetail = "0 B / 0 B";
            Servers.Add(server);
        }

        Passphrase = await _secretStore.LoadPassphraseAsync();
        ApiKeyDestinationFolder = config.ApiKeyDestinationFolder;
        if (string.IsNullOrWhiteSpace(ApiKeyDestinationFolder))
        {
            ApiKeyDestinationFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "VChannel", "ApiKeys");
        }

        SyncApiKeySourceSelectionFlags(string.IsNullOrWhiteSpace(config.ApiKeySourcePath)
            ? ApiKeyRootSourcePath
            : config.ApiKeySourcePath);

        RestoreDestinationPath = RestoreDestinationOptions.First();
        SetCurrentPage(home: true, backup: false, restore: false, apiKeys: false);
        RefreshRestorePreview();
        RefreshApiKeyFiles();
        LogText = $"Loaded settings from: {_settingsService.GetConfigPath()}";
        RaiseAllCanExecuteChanges();
    }

    public void SetOwnerWindow(Window ownerWindow)
    {
        _ownerWindow = ownerWindow;
    }

    private void AddServer()
    {
        var server = new ServerConfig
        {
            Name = $"Server{Servers.Count + 1}",
            RemoteDataPath = "/root/shadowbox/persisted-state/prometheus/data",
            RemoteConfigPath = "/root/shadowbox/persisted-state/shadowbox_config.json",
            Status = "Idle",
            ProgressPercent = 0,
            ProgressDetail = "0 B / 0 B"
        };

        Servers.Add(server);
        SelectedServer = server;
    }

    private void NavigateHome()
    {
        SetCurrentPage(home: true, backup: false, restore: false, apiKeys: false);
    }

    private void NavigateBackupPage()
    {
        SetCurrentPage(home: false, backup: true, restore: false, apiKeys: false);
    }

    private void NavigateRestorePage()
    {
        SetCurrentPage(home: false, backup: false, restore: true, apiKeys: false);
    }

    private void NavigateApiKeysPage()
    {
        SetCurrentPage(home: false, backup: false, restore: false, apiKeys: true);
    }

    private void SetCurrentPage(bool home, bool backup, bool restore, bool apiKeys)
    {
        IsHomePage = home;
        IsBackupPage = backup;
        IsRestorePage = restore;
        IsApiKeysPage = apiKeys;
    }

    private void SyncApiKeySourceSelectionFlags(string sourcePath)
    {
        var normalized = NormalizeRemotePath(sourcePath);
        var isRoot = string.Equals(normalized, ApiKeyRootSourcePath, StringComparison.Ordinal);
        var isOutline = string.Equals(normalized, ApiKeyOutlineSourcePath, StringComparison.Ordinal);

        SetProperty(ref _isApiKeyRootSourceSelected, isRoot, nameof(IsApiKeyRootSourceSelected));
        SetProperty(ref _isApiKeyOutlineSourceSelected, isOutline, nameof(IsApiKeyOutlineSourceSelected));
    }

    private void SyncRestoreDestinationSelectionFlags(string destinationPath)
    {
        var normalized = destinationPath?.Trim() ?? string.Empty;
        var isRoot = string.Equals(normalized, RootRestorePath, StringComparison.Ordinal);
        var isOutline = string.Equals(normalized, OutlineRestorePath, StringComparison.Ordinal);

        SetProperty(ref _isRootRestoreDestinationSelected, isRoot, nameof(IsRootRestoreDestinationSelected));
        SetProperty(ref _isOutlineRestoreDestinationSelected, isOutline, nameof(IsOutlineRestoreDestinationSelected));
    }

    public void AddServer(ServerConfig server)
    {
        server.Status = "Idle";
        server.ProgressPercent = 0;
        server.ProgressDetail = "0 B / 0 B";
        Servers.Add(server);
        SelectedServer = server;
        RaiseAllCanExecuteChanges();
    }

    private void DeleteSelectedServer()
    {
        if (SelectedServer is null)
        {
            return;
        }

        Servers.Remove(SelectedServer);
        SelectedServer = null;
        RecalculateOverallProgress();
        RaiseAllCanExecuteChanges();
    }

    private void DeleteAllServers()
    {
        if (Servers.Count == 0)
        {
            return;
        }

        var confirm = ShowThemedConfirmation(
            "Confirm",
            "Delete all configured servers?");

        if (!confirm)
        {
            return;
        }

        Servers.Clear();
        SelectedServer = null;
        RecalculateOverallProgress();
        LogText = $"[{DateTime.Now:HH:mm:ss}] Cleared all servers.";
        RaiseAllCanExecuteChanges();
    }

    private async Task SaveSettingsAsync()
    {
        try
        {
            IsBusy = true;

            if (string.IsNullOrWhiteSpace(BaseBackupDirectory))
            {
                BaseBackupDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "VChannel", "Premium");
            }

            var config = new AppConfig
            {
                WinScpAssemblyPath = WinScpAssemblyPath.Trim(),
                PrivateKeyPath = PrivateKeyPath.Trim(),
                BaseBackupDirectory = BaseBackupDirectory.Trim(),
                ApiKeyDestinationFolder = ApiKeyDestinationFolder.Trim(),
                ApiKeySourcePath = GetSelectedApiKeySourcePath(),
                RetentionCount = Math.Max(1, RetentionCount),
                BackupConfigOnly = BackupConfigOnly,
                BackupServerConfigOnly = BackupServerConfigOnly,
                RestoreConfigOnly = RestoreConfigOnly,
                RestoreServerConfigOnly = RestoreServerConfigOnly,
                Servers = Servers.ToList()
            };

            await _settingsService.SaveAsync(config);
            await _secretStore.SavePassphraseAsync(Passphrase);

            LogText = $"Settings saved at {DateTime.Now:HH:mm:ss}";
        }
        catch (Exception ex)
        {
            ShowThemedDialog("Save Error", $"Failed to save settings.\n{ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task StartBackupAsync()
    {
        if (Servers.Count == 0)
        {
            return;
        }

        await RunBackupAsync(Servers.ToList(), "Backup");
    }

    private async Task StartSelectedBackupAsync()
    {
        if (SelectedServer is null)
        {
            ShowThemedDialog("No Server Selected", "Select a server from Server Management before running single backup.");
            return;
        }

        await RunBackupAsync(new List<ServerConfig> { SelectedServer }, "Single Server Backup");
    }

    private async Task RunBackupAsync(IReadOnlyList<ServerConfig> targetServers, string operationLabel)
    {
        if (!ValidateSettings())
        {
            return;
        }

        await SaveSettingsAsync();

        var logRoot = Path.Combine(BaseBackupDirectory, "logs");
        Directory.CreateDirectory(logRoot);

        try
        {
            IsBusy = true;
            OverallProgress = 0;
            _backupCts = new CancellationTokenSource();
            var sb = new StringBuilder();
            var successCount = 0;
            var failedCount = 0;
            var canceledCount = 0;

            void UpdateOperationProgress()
            {
                if (targetServers.Count == 0)
                {
                    OverallProgress = 0;
                    return;
                }

                OverallProgress = (int)Math.Round(targetServers.Average(s => s.ProgressPercent));
            }

            foreach (var server in targetServers)
            {
                if (_backupCts.IsCancellationRequested)
                {
                    server.Status = "Cancelled";
                    canceledCount++;
                    continue;
                }

                server.Status = "Running";
                server.ProgressPercent = 0;
                server.ProgressDetail = BackupConfigOnly ? "Config file only" : "0 B / 0 B";
                var zipProcessLogged = false;
                var lastLoggedMessage = string.Empty;

                var progress = new Progress<ServerBackupProgress>(p =>
                {
                    if (!string.Equals(p.ServerName, server.Name, StringComparison.OrdinalIgnoreCase))
                    {
                        return;
                    }

                    server.Status = p.Status;
                    if (p.TransferredBytes.HasValue && p.TotalBytes.HasValue && p.TotalBytes.Value > 0)
                    {
                        server.ProgressPercent = (int)Math.Round((p.TransferredBytes.Value / (double)p.TotalBytes.Value) * 100d);
                        server.ProgressDetail = $"{FormatSize(p.TransferredBytes.Value)} / {FormatSize(p.TotalBytes.Value)}";
                    }
                    else
                    {
                        server.ProgressPercent = p.ProgressPercent;
                    }

                    if (server.ProgressPercent >= 100)
                    {
                        server.ProgressDetail = "Complete";
                    }

                    UpdateOperationProgress();

                    if (!string.IsNullOrWhiteSpace(p.Message))
                    {
                        if (string.Equals(p.Message, "Transferring data", StringComparison.OrdinalIgnoreCase))
                        {
                            return;
                        }

                        if (string.Equals(p.Message, "Creating selective zip", StringComparison.OrdinalIgnoreCase))
                        {
                            if (zipProcessLogged)
                            {
                                return;
                            }

                            zipProcessLogged = true;
                        }

                        if (string.Equals(lastLoggedMessage, p.Message, StringComparison.Ordinal))
                        {
                            return;
                        }

                        lastLoggedMessage = p.Message;
                        sb.AppendLine($"[{DateTime.Now:HH:mm:ss}] {server.Name}: {p.Message}");
                        LogText = sb.ToString();
                    }
                });

                try
                {
                    var config = new AppConfig
                    {
                        WinScpAssemblyPath = WinScpAssemblyPath,
                        PrivateKeyPath = PrivateKeyPath,
                        BaseBackupDirectory = BaseBackupDirectory,
                        ApiKeyDestinationFolder = ApiKeyDestinationFolder,
                        ApiKeySourcePath = GetSelectedApiKeySourcePath(),
                        RetentionCount = RetentionCount,
                        BackupConfigOnly = BackupConfigOnly,
                        RestoreConfigOnly = RestoreConfigOnly,
                        Servers = Servers.ToList()
                    };

                    await _backupService.RunServerBackupAsync(
                        server,
                        config,
                        Passphrase,
                        logRoot,
                        progress,
                        BackupConfigOnly,
                        BackupServerConfigOnly,
                        _backupCts.Token);

                    sb.AppendLine($"[{DateTime.Now:HH:mm:ss}] {server.Name}: Success");
                    successCount++;
                }
                catch (OperationCanceledException)
                {
                    server.Status = "Cancelled";
                    server.ProgressPercent = 0;
                    server.ProgressDetail = "Cancelled";
                    canceledCount++;
                    sb.AppendLine($"[{DateTime.Now:HH:mm:ss}] {server.Name}: Cancelled");
                }
                catch (Exception ex)
                {
                    server.Status = "Failed";
                    server.ProgressPercent = 100;
                    server.ProgressDetail = "Failed";
                    failedCount++;
                    sb.AppendLine($"[{DateTime.Now:HH:mm:ss}] {server.Name}: Failed - {ex.Message}");
                }

                UpdateOperationProgress();

                LogText = sb.ToString();
            }

            if (canceledCount == 0)
            {
                SystemSounds.Asterisk.Play();
                ShowThemedNotification(
                    $"{operationLabel} Completed",
                    $"{operationLabel} completed.\nSuccess: {successCount}\nFailed: {failedCount}");
            }
            else
            {
                SystemSounds.Exclamation.Play();
                ShowThemedNotification(
                    $"{operationLabel} Stopped",
                    $"{operationLabel} stopped by user.\nSuccess: {successCount}\nFailed: {failedCount}\nCancelled: {canceledCount}");
            }
        }
        finally
        {
            _backupCts?.Dispose();
            _backupCts = null;
            IsBusy = false;
        }
    }

    private async Task StartRestoreAsync()
    {
        if (!ValidateRestoreSettings(requireExistingFiles: !IsRestoreDryRun))
        {
            return;
        }

        await SaveSettingsAsync();

        try
        {
            IsBusy = true;

            var request = CreateRestoreRequest();
            RestoreCommandPreview = string.Join(Environment.NewLine, _restoreService.BuildRestoreScriptLines(request, maskSensitiveValues: true));

            if (IsRestoreDryRun)
            {
                RestoreSessionStatus = "Preview";
                RestoreTerminalText = RestoreCommandPreview;
                SystemSounds.Asterisk.Play();
                ShowThemedNotification("Restore Dry Run", "Preview generated. Disable dry-run to execute restore.");
                return;
            }

            var restoreConfirmed = ShowThemedConfirmation(
                "Confirm Restore Execution",
                BuildRestoreRiskChecklist());

            if (!restoreConfirmed)
            {
                RestoreSessionStatus = "Cancelled";
                AppendRestoreTerminalLine("Restore execution cancelled from confirmation dialog.");
                return;
            }

            IsRestoreRunning = true;
            RestoreSessionStatus = "Running";
            RestoreTerminalText = string.Empty;
            _restoreCts = new CancellationTokenSource();

            AppendRestoreTerminalLine("Restore session started.");

            var terminalOutput = new Progress<string>(line =>
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    return;
                }

                AppendRestoreTerminalLine(line.Trim());
            });

            await _restoreService.RunServerRestoreAsync(request, terminalOutput, _restoreCts.Token);

            RestoreSessionStatus = "Completed";
            AppendRestoreTerminalLine("Restore completed successfully.");
            SystemSounds.Asterisk.Play();
            ShowThemedNotification("Restore Completed", "Backup data and configuration were restored successfully.");
        }
        catch (OperationCanceledException)
        {
            RestoreSessionStatus = "Aborted";
            AppendRestoreTerminalLine("Restore aborted by user.");
            SystemSounds.Exclamation.Play();
            ShowThemedNotification("Restore Aborted", "Restore process was aborted.");
        }
        catch (Exception ex)
        {
            RestoreSessionStatus = "Failed";
            AppendRestoreTerminalLine($"Restore failed: {ex.Message}");
            SystemSounds.Hand.Play();
            ShowThemedNotification("Restore Failed", ex.Message);
        }
        finally
        {
            _restoreCts?.Dispose();
            _restoreCts = null;
            IsRestoreRunning = false;
            IsBusy = false;
        }
    }

    private async Task StartServerHardeningAsync()
    {
        if (!ValidateServerHardeningSettings())
        {
            return;
        }

        await SaveSettingsAsync();

        try
        {
            IsBusy = true;

            var request = CreateRestoreRequest();
            RestoreCommandPreview = string.Join(Environment.NewLine, _restoreService.BuildServerHardeningScriptLines(request, maskSensitiveValues: true));

            var confirmed = ShowThemedConfirmation(
                "Confirm Server Tuning",
                BuildServerHardeningChecklist());

            if (!confirmed)
            {
                RestoreSessionStatus = "Cancelled";
                AppendRestoreTerminalLine("Server tuning cancelled from confirmation dialog.");
                return;
            }

            IsRestoreRunning = true;
            RestoreSessionStatus = "Hardening";
            RestoreTerminalText = string.Empty;
            _restoreCts = new CancellationTokenSource();

            AppendRestoreTerminalLine("Server tuning started.");

            var terminalOutput = new Progress<string>(line =>
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    return;
                }

                AppendRestoreTerminalLine(line.Trim());
            });

            await _restoreService.RunServerHardeningAsync(request, terminalOutput, _restoreCts.Token);

            RestoreSessionStatus = "Hardened";
            AppendRestoreTerminalLine("Server tuning completed successfully.");
            SystemSounds.Asterisk.Play();
            ShowThemedNotification("Server Tuning Completed", "Swap and BBR tuning were applied successfully.");
        }
        catch (OperationCanceledException)
        {
            RestoreSessionStatus = "Aborted";
            AppendRestoreTerminalLine("Server tuning aborted by user.");
            SystemSounds.Exclamation.Play();
            ShowThemedNotification("Server Tuning Aborted", "Server tuning process was aborted.");
        }
        catch (Exception ex)
        {
            RestoreSessionStatus = "Failed";
            AppendRestoreTerminalLine($"Server tuning failed: {ex.Message}");
            SystemSounds.Hand.Play();
            ShowThemedNotification("Server Tuning Failed", ex.Message);
        }
        finally
        {
            _restoreCts?.Dispose();
            _restoreCts = null;
            IsRestoreRunning = false;
            IsBusy = false;
        }
    }

    private void StopBackup()
    {
        _backupCts?.Cancel();
        LogText += $"{Environment.NewLine}[{DateTime.Now:HH:mm:ss}] Stop requested by user.";
        StopBackupCommand.RaiseCanExecuteChanged();
    }

    private void AbortRestore()
    {
        _restoreCts?.Cancel();
        AppendRestoreTerminalLine("Abort requested by user.");
        AbortRestoreCommand.RaiseCanExecuteChanged();
    }

    private void EndRestoreSession()
    {
        if (IsRestoreRunning)
        {
            return;
        }

        RestoreTerminalText = string.Empty;
        RestoreSessionStatus = "Idle";
    }

    private RestoreRequest CreateRestoreRequest()
    {
        return new RestoreRequest
        {
            ServerIpAddress = RestoreServerIpAddress.Trim(),
            DataZipFilePath = RestoreDataZipFilePath.Trim(),
            ConfigFilePath = ResolveRestoreConfigFilePath(),
            ServerConfigFilePath = ResolveRestoreServerConfigFilePath(),
            DestinationPath = RestoreDestinationPath.Trim(),
            ConfigOnly = RestoreConfigOnly,
            ServerConfigOnly = RestoreServerConfigOnly,
            WinScpAssemblyPath = WinScpAssemblyPath.Trim(),
            PrivateKeyPath = PrivateKeyPath.Trim(),
            Passphrase = Passphrase
        };
    }

    private string ResolveRestoreConfigFilePath()
    {
        var configuredPath = RestoreConfigFilePath?.Trim();
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            return configuredPath;
        }

        if (RestoreConfigOnly)
        {
            return string.Empty;
        }

        var dataZipDirectory = Path.GetDirectoryName(RestoreDataZipFilePath?.Trim() ?? string.Empty);
        if (string.IsNullOrWhiteSpace(dataZipDirectory))
        {
            return string.Empty;
        }

        var preferredConfigPath = Path.Combine(dataZipDirectory, ServerConfigFileName);
        if (File.Exists(preferredConfigPath))
        {
            return preferredConfigPath;
        }

        var legacyConfigPath = Path.Combine(dataZipDirectory, "shadowbox_config.json");
        if (File.Exists(legacyConfigPath))
        {
            return legacyConfigPath;
        }

        return preferredConfigPath;
    }

    private string ResolveRestoreServerConfigFilePath()
    {
        var configuredPath = RestoreServerConfigFilePath?.Trim();
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            return configuredPath;
        }

        return string.Empty;
    }

    private void RefreshRestorePreview()
    {
        RefreshRestorePreview(showHint: false);
    }

    private void RefreshRestorePreview(bool showHint)
    {
        var request = CreateRestoreRequest();
        RestoreCommandPreview = string.Join(Environment.NewLine, _restoreService.BuildRestoreScriptLines(request, maskSensitiveValues: true));

        if (showHint)
        {
            AppendRestoreTerminalLine("Restore command preview refreshed.");
        }
    }

    private string BuildRestoreRiskChecklist()
    {
        if (RestoreConfigOnly && RestoreServerConfigOnly)
        {
            return string.Join(Environment.NewLine,
                "This will execute combined config restore actions on the target server:",
                "",
                "1. Upload config file and shadowbox_server_config.json.",
                "2. Restart docker container: shadowbox.",
                "",
                "Continue with live restore execution?");
        }

        if (RestoreServerConfigOnly)
        {
            return string.Join(Environment.NewLine,
                "This will execute server-config-only restore actions on the target server:",
                "",
                "1. Upload shadowbox_server_config.json.",
                "2. Restart docker container: shadowbox.",
                "",
                "Continue with live restore execution?");
        }

        if (RestoreConfigOnly)
        {
            return string.Join(Environment.NewLine,
                "This will execute config-only restore actions on the target server:",
                "",
                "1. Upload and overwrite config file in destination path.",
                "2. Restart docker container: shadowbox.",
                "",
                "Continue with live restore execution?");
        }

        return string.Join(Environment.NewLine,
            "This will execute restore actions on the target server:",
            "",
            "1. Install unzip package.",
            "2. Stop docker container: shadowbox.",
            "3. Upload and overwrite backup config/json and data zip in destination path.",
            "4. Remove old prometheus block/runtime files and copy restored data.",
            "5. Start docker container: shadowbox.",
            "",
            "Continue with live restore execution?");
    }

    private static string BuildServerHardeningChecklist()
    {
        return string.Join(Environment.NewLine,
            "This will execute one-time server tuning actions:",
            "",
            "1. Create and enable 2GB /swapfile if missing.",
            "2. Persist swap in /etc/fstab.",
            "3. Set vm.swappiness=10.",
            "4. Enable BBR settings in /etc/sysctl.conf.",
            "",
            "Continue with server tuning?");
    }

    private void AppendRestoreTerminalLine(string line)
    {
        var prefixed = $"[{DateTime.Now:HH:mm:ss}] {line}";
        RestoreTerminalText = string.IsNullOrWhiteSpace(RestoreTerminalText)
            ? prefixed
            : $"{RestoreTerminalText}{Environment.NewLine}{prefixed}";
    }

    private void ShowThemedNotification(string title, string message)
    {
        if (_ownerWindow is MainWindow mainWindow)
        {
            mainWindow.ShowThemedDialog(title, message);
            return;
        }

        ThemedDialog.Show(title, message);
    }

    private void ShowThemedDialog(string title, string message)
    {
        ShowThemedNotification(title, message);
    }

    private bool ShowThemedConfirmation(string title, string message)
    {
        if (_ownerWindow is MainWindow mainWindow)
        {
            return mainWindow.ShowThemedConfirmation(title, message);
        }

        return ThemedDialog.ShowConfirmation(title, message);
    }

    private bool ValidateSettings()
    {
        if (Servers.Count == 0)
        {
            ShowThemedDialog("Validation", "Add at least one server.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(WinScpAssemblyPath) || !File.Exists(WinScpAssemblyPath))
        {
            ShowThemedDialog("Validation", "Set a valid WinSCPnet.dll path.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(PrivateKeyPath) || !File.Exists(PrivateKeyPath))
        {
            ShowThemedDialog("Validation", "Set a valid private key (.ppk) path.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(Passphrase))
        {
            ShowThemedDialog("Validation", "Enter private key passphrase.");
            return false;
        }

        foreach (var server in Servers)
        {
            if (string.IsNullOrWhiteSpace(server.Name) ||
                string.IsNullOrWhiteSpace(server.IpAddress) ||
                string.IsNullOrWhiteSpace(server.RemoteConfigPath))
            {
                ShowThemedDialog("Validation", "Each server requires Name, IP, and Remote Config Path.");
                return false;
            }

            if (!BackupConfigOnly && string.IsNullOrWhiteSpace(server.RemoteDataPath))
            {
                ShowThemedDialog("Validation", "Each server requires Remote Data Path when config-only backup is disabled.");
                return false;
            }
        }

        return true;
    }

    private bool ValidateRestoreSettings(bool requireExistingFiles)
    {
        if (string.IsNullOrWhiteSpace(RestoreServerIpAddress))
        {
            ShowThemedDialog("Validation", "Enter target server IP address.");
            return false;
        }

        if (!RestoreConfigOnly && !RestoreServerConfigOnly && (string.IsNullOrWhiteSpace(RestoreDataZipFilePath) || (requireExistingFiles && !File.Exists(RestoreDataZipFilePath))))
        {
            var message = requireExistingFiles
                ? "Select a valid data zip file."
                : "Enter data zip file path for preview.";
            ShowThemedDialog("Validation", message);
            return false;
        }

        if (RestoreConfigOnly && RestoreServerConfigOnly)
        {
            if (string.IsNullOrWhiteSpace(RestoreConfigFilePath) || (requireExistingFiles && !File.Exists(RestoreConfigFilePath)))
            {
                var message = requireExistingFiles
                    ? "Select a valid config file."
                    : "Enter config file path for preview.";
                ShowThemedDialog("Validation", message);
                return false;
            }

            if (string.IsNullOrWhiteSpace(RestoreServerConfigFilePath) || (requireExistingFiles && !File.Exists(RestoreServerConfigFilePath)))
            {
                var message = requireExistingFiles
                    ? "Select a valid server config file."
                    : "Enter server config file path for preview.";
                ShowThemedDialog("Validation", message);
                return false;
            }
        }
        else if (RestoreConfigOnly)
        {
            if (string.IsNullOrWhiteSpace(RestoreConfigFilePath) || (requireExistingFiles && !File.Exists(RestoreConfigFilePath)))
            {
                var message = requireExistingFiles
                    ? "Select a valid config file."
                    : "Enter config file path for preview.";
                ShowThemedDialog("Validation", message);
                return false;
            }
        }
        else if (RestoreServerConfigOnly)
        {
            if (string.IsNullOrWhiteSpace(RestoreServerConfigFilePath) || (requireExistingFiles && !File.Exists(RestoreServerConfigFilePath)))
            {
                var message = requireExistingFiles
                    ? "Select a valid server config file."
                    : "Enter server config file path for preview.";
                ShowThemedDialog("Validation", message);
                return false;
            }
        }
        else
        {
            var resolvedConfigFilePath = ResolveRestoreConfigFilePath();
            if (string.IsNullOrWhiteSpace(resolvedConfigFilePath) || (requireExistingFiles && !File.Exists(resolvedConfigFilePath)))
            {
                var message = requireExistingFiles
                    ? $"Could not locate {ServerConfigFileName} next to the selected data zip."
                    : $"{ServerConfigFileName} will be inferred from the selected data zip folder for preview.";
                ShowThemedDialog("Validation", message);
                return false;
            }
        }

        if (string.IsNullOrWhiteSpace(WinScpAssemblyPath) || (requireExistingFiles && !File.Exists(WinScpAssemblyPath)))
        {
            var message = requireExistingFiles
                ? "Set a valid WinSCPnet.dll path in Configuration."
                : "Enter WinSCPnet.dll path in Configuration for preview.";
            ShowThemedDialog("Validation", message);
            return false;
        }

        if (string.IsNullOrWhiteSpace(PrivateKeyPath) || (requireExistingFiles && !File.Exists(PrivateKeyPath)))
        {
            var message = requireExistingFiles
                ? "Set a valid private key (.ppk) path on Restore page."
                : "Enter private key (.ppk) path on Restore page for preview.";
            ShowThemedDialog("Validation", message);
            return false;
        }

        if (string.IsNullOrWhiteSpace(Passphrase))
        {
            ShowThemedDialog("Validation", "Enter private key passphrase on Restore page.");
            return false;
        }

        if (!RestoreDestinationOptions.Contains(RestoreDestinationPath))
        {
            RestoreDestinationPath = RootRestorePath;
        }

        return true;
    }

    private bool ValidateServerHardeningSettings()
    {
        if (string.IsNullOrWhiteSpace(RestoreServerIpAddress))
        {
            ShowThemedDialog("Validation", "Enter target server IP address.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(WinScpAssemblyPath) || !File.Exists(WinScpAssemblyPath))
        {
            ShowThemedDialog("Validation", "Set a valid WinSCPnet.dll path in Configuration.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(PrivateKeyPath) || !File.Exists(PrivateKeyPath))
        {
            ShowThemedDialog("Validation", "Set a valid private key (.ppk) path on Restore page.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(Passphrase))
        {
            ShowThemedDialog("Validation", "Enter private key passphrase on Restore page.");
            return false;
        }

        return true;
    }

    private async Task ImportServersAsync()
    {
        var dialog = new WpfOpenFileDialog
        {
            Filter = "JSON Files (*.json)|*.json|All Files (*.*)|*.*"
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            await using var stream = File.OpenRead(dialog.FileName);
            var imported = await JsonSerializer.DeserializeAsync<List<ServerConfig>>(stream)
                ?? new List<ServerConfig>();

            Servers.Clear();
            foreach (var server in imported)
            {
                server.Status = "Idle";
                server.ProgressPercent = 0;
                Servers.Add(server);
            }

            LogText = $"Imported {Servers.Count} server(s) from {dialog.FileName}";
            RaiseAllCanExecuteChanges();
        }
        catch (Exception ex)
        {
            ShowThemedDialog("Import Error", $"Import failed.\n{ex.Message}");
        }
    }

    private async Task ExportServersAsync()
    {
        var dialog = new WpfSaveFileDialog
        {
            Filter = "JSON Files (*.json)|*.json",
            FileName = "servers.json"
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            await using var stream = File.Create(dialog.FileName);
            await JsonSerializer.SerializeAsync(stream, Servers.ToList(), new JsonSerializerOptions { WriteIndented = true });
            LogText = $"Exported {Servers.Count} server(s) to {dialog.FileName}";
        }
        catch (Exception ex)
        {
            ShowThemedDialog("Export Error", $"Export failed.\n{ex.Message}");
        }
    }

    private async Task ExportConfigurationAsync()
    {
        var dialog = new WpfSaveFileDialog
        {
            Filter = "VC Sync Config (*.vcsync.json)|*.vcsync.json|JSON Files (*.json)|*.json",
            FileName = "vc-sync-config.vcsync.json"
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            var config = new AppConfig
            {
                WinScpAssemblyPath = WinScpAssemblyPath.Trim(),
                PrivateKeyPath = PrivateKeyPath.Trim(),
                BaseBackupDirectory = BaseBackupDirectory.Trim(),
                RetentionCount = Math.Max(1, RetentionCount),
                BackupConfigOnly = BackupConfigOnly,
                BackupServerConfigOnly = BackupServerConfigOnly,
                RestoreConfigOnly = RestoreConfigOnly,
                RestoreServerConfigOnly = RestoreServerConfigOnly,
                Servers = Servers.ToList()
            };

            await using var stream = File.Create(dialog.FileName);
            await JsonSerializer.SerializeAsync(stream, config, new JsonSerializerOptions { WriteIndented = true });
            LogText = $"Exported configuration to {dialog.FileName}";
        }
        catch (Exception ex)
        {
            ShowThemedDialog("Export Error", $"Configuration export failed.\n{ex.Message}");
        }
    }

    private async Task ImportConfigurationAsync()
    {
        var dialog = new WpfOpenFileDialog
        {
            Filter = "VC Sync Config (*.vcsync.json)|*.vcsync.json|JSON Files (*.json)|*.json|All Files (*.*)|*.*"
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            await using var stream = File.OpenRead(dialog.FileName);
            var config = await JsonSerializer.DeserializeAsync<AppConfig>(stream)
                ?? throw new InvalidOperationException("Configuration file is invalid.");

            WinScpAssemblyPath = config.WinScpAssemblyPath;
            PrivateKeyPath = config.PrivateKeyPath;
            BaseBackupDirectory = config.BaseBackupDirectory;
            ApiKeyDestinationFolder = config.ApiKeyDestinationFolder;
            if (string.IsNullOrWhiteSpace(ApiKeyDestinationFolder))
            {
                ApiKeyDestinationFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "VChannel", "ApiKeys");
            }

            SyncApiKeySourceSelectionFlags(string.IsNullOrWhiteSpace(config.ApiKeySourcePath)
                ? ApiKeyRootSourcePath
                : config.ApiKeySourcePath);

            RetentionCount = config.RetentionCount <= 0 ? 1 : config.RetentionCount;
            BackupConfigOnly = config.BackupConfigOnly;
            BackupServerConfigOnly = config.BackupServerConfigOnly;
            RestoreConfigOnly = config.RestoreConfigOnly;
            RestoreServerConfigOnly = config.RestoreServerConfigOnly;

            Servers.Clear();
            foreach (var server in config.Servers)
            {
                server.Status = "Idle";
                server.ProgressPercent = 0;
                server.ProgressDetail = "0 B / 0 B";
                Servers.Add(server);
            }

            await _settingsService.SaveAsync(new AppConfig
            {
                WinScpAssemblyPath = WinScpAssemblyPath,
                PrivateKeyPath = PrivateKeyPath,
                BaseBackupDirectory = BaseBackupDirectory,
                RetentionCount = RetentionCount,
                BackupConfigOnly = BackupConfigOnly,
                BackupServerConfigOnly = BackupServerConfigOnly,
                RestoreConfigOnly = RestoreConfigOnly,
                RestoreServerConfigOnly = RestoreServerConfigOnly,
                Servers = Servers.ToList()
            });

            RecalculateOverallProgress();
            RaiseAllCanExecuteChanges();
            LogText = $"Imported configuration from {dialog.FileName}";
            RefreshApiKeyFiles();
        }
        catch (Exception ex)
        {
            ShowThemedDialog("Import Error", $"Configuration import failed.\n{ex.Message}");
        }
    }

    private void BrowseAssembly()
    {
        var dialog = new WpfOpenFileDialog
        {
            Filter = "WinSCP .NET Assembly (WinSCPnet.dll)|WinSCPnet.dll|DLL Files (*.dll)|*.dll|All Files (*.*)|*.*"
        };

        if (dialog.ShowDialog() == true)
        {
            WinScpAssemblyPath = dialog.FileName;
        }
    }

    private void BrowsePrivateKey()
    {
        var dialog = new WpfOpenFileDialog
        {
            Filter = "PuTTY Private Key (*.ppk)|*.ppk|All Files (*.*)|*.*"
        };

        if (dialog.ShowDialog() == true)
        {
            PrivateKeyPath = dialog.FileName;
        }
    }

    private void BrowseBaseDirectory()
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Select base backup directory"
        };

        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            BaseBackupDirectory = dialog.SelectedPath;
        }
    }

    private void BrowseRestoreDataZip()
    {
        var dialog = new WpfOpenFileDialog
        {
            Filter = "Zip Files (*.zip)|*.zip|All Files (*.*)|*.*"
        };

        if (dialog.ShowDialog() == true)
        {
            RestoreDataZipFilePath = dialog.FileName;
        }
    }

    private void BrowseRestoreConfig()
    {
        var dialog = new WpfOpenFileDialog
        {
            Filter = "JSON Files (*.json)|*.json|All Files (*.*)|*.*"
        };

        if (dialog.ShowDialog() == true)
        {
            RestoreConfigFilePath = dialog.FileName;
        }
    }

    private void BrowseRestoreServerConfig()
    {
        var dialog = new WpfOpenFileDialog
        {
            Filter = "JSON Files (*.json)|*.json|All Files (*.*)|*.*"
        };

        if (dialog.ShowDialog() == true)
        {
            RestoreServerConfigFilePath = dialog.FileName;
        }
    }

    private async Task GenerateApiKeyAsync()
    {
        if (!ValidateApiKeyGenerationSettings())
        {
            return;
        }

        try
        {
            IsBusy = true;
            var apiKeyJson = await _apiKeyService.GenerateApiKeyAsync(
                WinScpAssemblyPath.Trim(),
                PrivateKeyPath.Trim(),
                Passphrase,
                ApiKeyServerIpAddress.Trim(),
                GetSelectedApiKeySourcePath(),
                CancellationToken.None);

            ApiKeyValue = apiKeyJson;
            LogText = $"Generated API key for {ApiKeyServerIpAddress.Trim()}";
        }
        catch (Exception ex)
        {
            ShowThemedDialog("API Key Error", $"API key generation failed.\n{ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task SaveApiKeyAsync()
    {
        if (!ValidateApiKeySaveSettings())
        {
            return;
        }

        try
        {
            IsBusy = true;

            var directory = ApiKeyDestinationFolder.Trim();
            Directory.CreateDirectory(directory);

            var fileName = BuildApiKeyFileName(ApiKeyServerName.Trim());
            var filePath = Path.Combine(directory, fileName);
            await File.WriteAllTextAsync(filePath, ApiKeyValue);

            RefreshApiKeyFiles();
            LogText = $"Saved API key to {filePath}";
        }
        catch (Exception ex)
        {
            ShowThemedDialog("API Key Error", $"Failed to save API key.\n{ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void CopyApiKeyToClipboard()
    {
        if (string.IsNullOrWhiteSpace(ApiKeyValue))
        {
            ShowThemedDialog("API Key", "Generate an API key before copying.");
            return;
        }

        System.Windows.Clipboard.SetText(ApiKeyValue);
        LogText = $"Copied API key to clipboard at {DateTime.Now:HH:mm:ss}";
    }

    private void ClearAndPasteApiKeyServerIp()
    {
        ApiKeyServerIpAddress = string.Empty;

        if (System.Windows.Clipboard.ContainsText())
        {
            ApiKeyServerIpAddress = System.Windows.Clipboard.GetText(System.Windows.TextDataFormat.Text).Trim();
        }
    }

    private void ClearAndPasteRestoreServerIp()
    {
        RestoreServerIpAddress = string.Empty;

        if (System.Windows.Clipboard.ContainsText())
        {
            RestoreServerIpAddress = System.Windows.Clipboard.GetText(System.Windows.TextDataFormat.Text).Trim();
        }
    }

    private void BrowseApiKeyDestinationFolder()
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Select API key destination folder"
        };

        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            ApiKeyDestinationFolder = dialog.SelectedPath;
            // Persist the last-used API key destination immediately so it's remembered
            _ = PersistApiKeyDestinationFolderAsync();
        }
    }

    private async Task PersistApiKeyDestinationFolderAsync()
    {
        try
        {
            var config = new AppConfig
            {
                WinScpAssemblyPath = WinScpAssemblyPath?.Trim() ?? string.Empty,
                PrivateKeyPath = PrivateKeyPath?.Trim() ?? string.Empty,
                BaseBackupDirectory = BaseBackupDirectory?.Trim() ?? string.Empty,
                ApiKeyDestinationFolder = ApiKeyDestinationFolder?.Trim() ?? string.Empty,
                ApiKeySourcePath = GetSelectedApiKeySourcePath(),
                RetentionCount = Math.Max(1, RetentionCount),
                BackupConfigOnly = BackupConfigOnly,
                BackupServerConfigOnly = BackupServerConfigOnly,
                RestoreConfigOnly = RestoreConfigOnly,
                RestoreServerConfigOnly = RestoreServerConfigOnly,
                Servers = Servers.ToList()
            };

            await _settingsService.SaveAsync(config);
            LogText = $"Saved API key destination at {DateTime.Now:HH:mm:ss}";
        }
        catch (Exception ex)
        {
            ShowThemedDialog("Save Error", $"Failed to persist API key destination.\n{ex.Message}");
        }
    }

    private void LoadApiKeyEntry(object? parameter)
    {
        if (parameter is not ApiKeyFileEntry entry || !File.Exists(entry.FilePath))
        {
            return;
        }

        ApiKeyServerName = entry.ServerName;
        ApiKeyServerIpAddress = entry.IpAddress;
        ApiKeyValue = entry.ApiKeyJson;
        LogText = $"Loaded API key from {entry.FileName}";
    }

    private void DeleteApiKeyEntry(object? parameter)
    {
        if (parameter is not ApiKeyFileEntry entry || !File.Exists(entry.FilePath))
        {
            return;
        }

        var confirm = ShowThemedConfirmation("Confirm", $"Delete {entry.FileName}?");

        if (!confirm)
        {
            return;
        }

        File.Delete(entry.FilePath);
        RefreshApiKeyFiles();
        LogText = $"Deleted API key file {entry.FileName}";
    }

    private void RefreshApiKeyFiles()
    {
        ApiKeyFiles.Clear();

        var directory = ApiKeyDestinationFolder?.Trim();
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return;
        }

        foreach (var filePath in Directory.EnumerateFiles(directory, "*-access.txt")
                     .OrderByDescending(File.GetCreationTime))
        {
            if (ApiKeyFileEntry.TryLoad(filePath, out var entry) && entry is not null)
            {
                ApiKeyFiles.Add(entry);
            }
        }
    }

    private string GetSelectedApiKeySourcePath()
    {
        return IsApiKeyOutlineSourceSelected ? ApiKeyOutlineSourcePath : ApiKeyRootSourcePath;
    }

    private bool ValidateApiKeyGenerationSettings()
    {
        if (string.IsNullOrWhiteSpace(ApiKeyServerIpAddress))
        {
            ShowThemedDialog("Validation", "Enter server IP address.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(WinScpAssemblyPath) || !File.Exists(WinScpAssemblyPath))
        {
            ShowThemedDialog("Validation", "Set a valid WinSCPnet.dll path.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(PrivateKeyPath) || !File.Exists(PrivateKeyPath))
        {
            ShowThemedDialog("Validation", "Set a valid private key (.ppk) path.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(Passphrase))
        {
            ShowThemedDialog("Validation", "Enter private key passphrase.");
            return false;
        }

        return true;
    }

    private bool ValidateApiKeySaveSettings()
    {
        if (string.IsNullOrWhiteSpace(ApiKeyValue))
        {
            ShowThemedDialog("Validation", "Generate an API key first.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(ApiKeyServerName))
        {
            ShowThemedDialog("Validation", "Enter a server name.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(ApiKeyDestinationFolder))
        {
            ShowThemedDialog("Validation", "Select a destination folder.");
            return false;
        }

        return true;
    }

    private static string BuildApiKeyFileName(string serverName)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var sanitized = new string(serverName.Where(character => !invalidChars.Contains(character)).ToArray()).Trim();
        if (string.IsNullOrWhiteSpace(sanitized))
        {
            sanitized = "server";
        }

        return $"{sanitized}-access.txt";
    }

    private static string NormalizeRemotePath(string remotePath)
    {
        var normalized = (remotePath ?? string.Empty).Trim().Replace('\\', '/');
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return string.Empty;
        }

        if (!normalized.EndsWith('/'))
        {
            normalized += "/";
        }

        return normalized;
    }

    private void RaiseAllCanExecuteChanges()
    {
        AddServerCommand.RaiseCanExecuteChanged();
        DeleteServerCommand.RaiseCanExecuteChanged();
        DeleteAllServersCommand.RaiseCanExecuteChanged();
        SaveSettingsCommand.RaiseCanExecuteChanged();
        StartBackupCommand.RaiseCanExecuteChanged();
        StartSelectedBackupCommand.RaiseCanExecuteChanged();
        StopBackupCommand.RaiseCanExecuteChanged();
        ImportServersCommand.RaiseCanExecuteChanged();
        ExportServersCommand.RaiseCanExecuteChanged();
        ImportConfigCommand.RaiseCanExecuteChanged();
        ExportConfigCommand.RaiseCanExecuteChanged();
        BrowseAssemblyCommand.RaiseCanExecuteChanged();
        BrowseKeyCommand.RaiseCanExecuteChanged();
        BrowseBaseDirectoryCommand.RaiseCanExecuteChanged();
        BrowseRestoreDataZipCommand.RaiseCanExecuteChanged();
        BrowseRestoreConfigCommand.RaiseCanExecuteChanged();
        BrowseRestoreServerConfigCommand.RaiseCanExecuteChanged();
        RefreshRestorePreviewCommand.RaiseCanExecuteChanged();
        ApplyServerTuningCommand.RaiseCanExecuteChanged();
        StartRestoreCommand.RaiseCanExecuteChanged();
        AbortRestoreCommand.RaiseCanExecuteChanged();
        EndRestoreSessionCommand.RaiseCanExecuteChanged();
        NavigateHomeCommand.RaiseCanExecuteChanged();
        NavigateBackupPageCommand.RaiseCanExecuteChanged();
        NavigateRestorePageCommand.RaiseCanExecuteChanged();
        NavigateApiKeysPageCommand.RaiseCanExecuteChanged();
        GenerateApiKeyCommand.RaiseCanExecuteChanged();
        SaveApiKeyCommand.RaiseCanExecuteChanged();
        CopyApiKeyCommand.RaiseCanExecuteChanged();
        ClearPasteApiKeyServerIpCommand.RaiseCanExecuteChanged();
        BrowseApiKeyDestinationCommand.RaiseCanExecuteChanged();
        ViewApiKeyCommand.RaiseCanExecuteChanged();
        DeleteApiKeyCommand.RaiseCanExecuteChanged();
    }

    private void RecalculateOverallProgress()
    {
        if (Servers.Count == 0)
        {
            OverallProgress = 0;
            return;
        }

        OverallProgress = (int)Math.Round(Servers.Average(s => s.ProgressPercent));
    }

    private static string FormatSize(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double size = Math.Max(0, bytes);
        int unitIndex = 0;

        while (size >= 1024 && unitIndex < units.Length - 1)
        {
            size /= 1024;
            unitIndex++;
        }

        return $"{size:0.##} {units[unitIndex]}";
    }
}
