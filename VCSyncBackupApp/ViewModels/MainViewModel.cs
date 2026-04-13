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
    private readonly string _appDataPath;
    private readonly SettingsService _settingsService;
    private readonly SecretStore _secretStore;
    private readonly BackupService _backupService;
    private Window? _ownerWindow;

    private ServerConfig? _selectedServer;
    private string _winScpAssemblyPath = string.Empty;
    private string _privateKeyPath = string.Empty;
    private string _passphrase = string.Empty;
    private string _baseBackupDirectory = string.Empty;
    private int _retentionCount = 1;
    private string _logText = string.Empty;
    private bool _isBusy;
    private int _overallProgress;
    private CancellationTokenSource? _backupCts;

    public MainViewModel()
    {
        _appDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VCSyncBackupApp");
        Directory.CreateDirectory(_appDataPath);

        _settingsService = new SettingsService(_appDataPath);
        _secretStore = new SecretStore(_appDataPath);
        _backupService = new BackupService();

        Servers = new ObservableCollection<ServerConfig>();

        AddServerCommand = new RelayCommand(AddServer, () => !IsBusy);
        DeleteServerCommand = new RelayCommand(DeleteSelectedServer, () => SelectedServer is not null && !IsBusy);
        DeleteAllServersCommand = new RelayCommand(DeleteAllServers, () => Servers.Count > 0 && !IsBusy);
        SaveSettingsCommand = new AsyncRelayCommand(SaveSettingsAsync, () => !IsBusy);
        StartBackupCommand = new AsyncRelayCommand(StartBackupAsync, () => Servers.Count > 0 && !IsBusy);
        StopBackupCommand = new RelayCommand(StopBackup, () => IsBusy);
        ImportServersCommand = new AsyncRelayCommand(ImportServersAsync, () => !IsBusy);
        ExportServersCommand = new AsyncRelayCommand(ExportServersAsync, () => Servers.Count > 0 && !IsBusy);
        ImportConfigCommand = new AsyncRelayCommand(ImportConfigurationAsync, () => !IsBusy);
        ExportConfigCommand = new AsyncRelayCommand(ExportConfigurationAsync, () => !IsBusy);
        BrowseAssemblyCommand = new RelayCommand(BrowseAssembly, () => !IsBusy);
        BrowseKeyCommand = new RelayCommand(BrowsePrivateKey, () => !IsBusy);
        BrowseBaseDirectoryCommand = new RelayCommand(BrowseBaseDirectory, () => !IsBusy);
    }

    public ObservableCollection<ServerConfig> Servers { get; }

    public ServerConfig? SelectedServer
    {
        get => _selectedServer;
        set
        {
            if (SetProperty(ref _selectedServer, value))
            {
                DeleteServerCommand.RaiseCanExecuteChanged();
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

    public RelayCommand AddServerCommand { get; }
    public RelayCommand DeleteServerCommand { get; }
    public RelayCommand DeleteAllServersCommand { get; }
    public AsyncRelayCommand SaveSettingsCommand { get; }
    public AsyncRelayCommand StartBackupCommand { get; }
    public RelayCommand StopBackupCommand { get; }
    public AsyncRelayCommand ImportServersCommand { get; }
    public AsyncRelayCommand ExportServersCommand { get; }
    public AsyncRelayCommand ImportConfigCommand { get; }
    public AsyncRelayCommand ExportConfigCommand { get; }
    public RelayCommand BrowseAssemblyCommand { get; }
    public RelayCommand BrowseKeyCommand { get; }
    public RelayCommand BrowseBaseDirectoryCommand { get; }

    public async Task InitializeAsync()
    {
        var config = await _settingsService.LoadAsync();

        WinScpAssemblyPath = config.WinScpAssemblyPath;
        PrivateKeyPath = config.PrivateKeyPath;
        BaseBackupDirectory = string.IsNullOrWhiteSpace(config.BaseBackupDirectory)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "VChannel", "Premium")
            : config.BaseBackupDirectory;
        RetentionCount = config.RetentionCount <= 0 ? 1 : config.RetentionCount;

        Servers.Clear();
        foreach (var server in config.Servers)
        {
            server.Status = "Idle";
            server.ProgressPercent = 0;
            server.ProgressDetail = "0 B / 0 B";
            Servers.Add(server);
        }

        Passphrase = await _secretStore.LoadPassphraseAsync();
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

        var confirm = WpfMessageBox.Show(
            "Delete all configured servers?",
            "Confirm",
            WpfMessageBoxButton.YesNo,
            WpfMessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes)
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
                RetentionCount = Math.Max(1, RetentionCount),
                Servers = Servers.ToList()
            };

            await _settingsService.SaveAsync(config);
            await _secretStore.SavePassphraseAsync(Passphrase);

            LogText = $"Settings saved at {DateTime.Now:HH:mm:ss}";
        }
        catch (Exception ex)
        {
            WpfMessageBox.Show($"Failed to save settings.\n{ex.Message}", "Save Error", WpfMessageBoxButton.OK, WpfMessageBoxImage.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task StartBackupAsync()
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

            foreach (var server in Servers)
            {
                if (_backupCts.IsCancellationRequested)
                {
                    server.Status = "Cancelled";
                    canceledCount++;
                    continue;
                }

                server.Status = "Running";
                server.ProgressPercent = 0;
                server.ProgressDetail = "0 B / 0 B";
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

                    RecalculateOverallProgress();

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
                        RetentionCount = RetentionCount,
                        Servers = Servers.ToList()
                    };

                    await _backupService.RunServerBackupAsync(
                        server,
                        config,
                        Passphrase,
                        logRoot,
                        progress,
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

                RecalculateOverallProgress();

                LogText = sb.ToString();
            }

            if (canceledCount == 0)
            {
                SystemSounds.Asterisk.Play();
                ShowThemedNotification(
                    "Backup Completed",
                    $"Backup completed.\nSuccess: {successCount}\nFailed: {failedCount}");
            }
            else
            {
                SystemSounds.Exclamation.Play();
                ShowThemedNotification(
                    "Backup Stopped",
                    $"Backup stopped by user.\nSuccess: {successCount}\nFailed: {failedCount}\nCancelled: {canceledCount}");
            }
        }
        finally
        {
            _backupCts?.Dispose();
            _backupCts = null;
            IsBusy = false;
        }
    }

    private void StopBackup()
    {
        _backupCts?.Cancel();
        LogText += $"{Environment.NewLine}[{DateTime.Now:HH:mm:ss}] Stop requested by user.";
        StopBackupCommand.RaiseCanExecuteChanged();
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

    private bool ValidateSettings()
    {
        if (Servers.Count == 0)
        {
            WpfMessageBox.Show("Add at least one server.", "Validation", WpfMessageBoxButton.OK, WpfMessageBoxImage.Warning);
            return false;
        }

        if (string.IsNullOrWhiteSpace(WinScpAssemblyPath) || !File.Exists(WinScpAssemblyPath))
        {
            WpfMessageBox.Show("Set a valid WinSCPnet.dll path.", "Validation", WpfMessageBoxButton.OK, WpfMessageBoxImage.Warning);
            return false;
        }

        if (string.IsNullOrWhiteSpace(PrivateKeyPath) || !File.Exists(PrivateKeyPath))
        {
            WpfMessageBox.Show("Set a valid private key (.ppk) path.", "Validation", WpfMessageBoxButton.OK, WpfMessageBoxImage.Warning);
            return false;
        }

        if (string.IsNullOrWhiteSpace(Passphrase))
        {
            WpfMessageBox.Show("Enter private key passphrase.", "Validation", WpfMessageBoxButton.OK, WpfMessageBoxImage.Warning);
            return false;
        }

        foreach (var server in Servers)
        {
            if (string.IsNullOrWhiteSpace(server.Name) ||
                string.IsNullOrWhiteSpace(server.IpAddress) ||
                string.IsNullOrWhiteSpace(server.RemoteDataPath) ||
                string.IsNullOrWhiteSpace(server.RemoteConfigPath))
            {
                WpfMessageBox.Show("Each server requires Name, IP, Remote Data Path, and Remote Config Path.", "Validation", WpfMessageBoxButton.OK, WpfMessageBoxImage.Warning);
                return false;
            }
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
            WpfMessageBox.Show($"Import failed.\n{ex.Message}", "Import Error", WpfMessageBoxButton.OK, WpfMessageBoxImage.Error);
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
            WpfMessageBox.Show($"Export failed.\n{ex.Message}", "Export Error", WpfMessageBoxButton.OK, WpfMessageBoxImage.Error);
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
                Servers = Servers.ToList()
            };

            await using var stream = File.Create(dialog.FileName);
            await JsonSerializer.SerializeAsync(stream, config, new JsonSerializerOptions { WriteIndented = true });
            LogText = $"Exported configuration to {dialog.FileName}";
        }
        catch (Exception ex)
        {
            WpfMessageBox.Show($"Configuration export failed.\n{ex.Message}", "Export Error", WpfMessageBoxButton.OK, WpfMessageBoxImage.Error);
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
            RetentionCount = config.RetentionCount <= 0 ? 1 : config.RetentionCount;

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
                Servers = Servers.ToList()
            });

            RecalculateOverallProgress();
            RaiseAllCanExecuteChanges();
            LogText = $"Imported configuration from {dialog.FileName}";
        }
        catch (Exception ex)
        {
            WpfMessageBox.Show($"Configuration import failed.\n{ex.Message}", "Import Error", WpfMessageBoxButton.OK, WpfMessageBoxImage.Error);
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

    private void RaiseAllCanExecuteChanges()
    {
        AddServerCommand.RaiseCanExecuteChanged();
        DeleteServerCommand.RaiseCanExecuteChanged();
        DeleteAllServersCommand.RaiseCanExecuteChanged();
        SaveSettingsCommand.RaiseCanExecuteChanged();
        StartBackupCommand.RaiseCanExecuteChanged();
        StopBackupCommand.RaiseCanExecuteChanged();
        ImportServersCommand.RaiseCanExecuteChanged();
        ExportServersCommand.RaiseCanExecuteChanged();
        ImportConfigCommand.RaiseCanExecuteChanged();
        ExportConfigCommand.RaiseCanExecuteChanged();
        BrowseAssemblyCommand.RaiseCanExecuteChanged();
        BrowseKeyCommand.RaiseCanExecuteChanged();
        BrowseBaseDirectoryCommand.RaiseCanExecuteChanged();
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
