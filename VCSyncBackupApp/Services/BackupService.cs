using System.IO.Compression;
using System.IO;
using System.Reflection;
using System.Text;
using System.Diagnostics;
using System.Text.RegularExpressions;
using VCSyncBackupApp.Models;

namespace VCSyncBackupApp.Services;

public sealed class BackupService
{
    private static readonly Regex PercentRegex = new(@"(?<pct>\d{1,3})%", RegexOptions.Compiled);
    private static readonly Regex CliTransferLineRegex = new(@"^\s*(?<name>[^|]+?)\s*\|.*\|\s*(?<pct>\d{1,3})%\s*$", RegexOptions.Compiled);

    public async Task RunServerBackupAsync(
        ServerConfig server,
        AppConfig config,
        string passphrase,
        string logRoot,
        IProgress<ServerBackupProgress>? progress,
        bool configOnly,
        CancellationToken cancellationToken)
    {
        var serverBase = Path.Combine(config.BaseBackupDirectory, server.Name);
        var dataFolder = Path.Combine(serverBase, "data");
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HHmm");
        var zipPath = Path.Combine(serverBase, $"data_{timestamp}.zip");
        var logFile = Path.Combine(logRoot, $"{server.Name}.log");

        Directory.CreateDirectory(serverBase);
        if (!configOnly)
        {
            Directory.CreateDirectory(dataFolder);
        }
        Directory.CreateDirectory(logRoot);

        var logger = new List<string>
        {
            $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Starting backup for {server.Name} ({server.IpAddress})"
        };

        try
        {
            progress?.Report(new ServerBackupProgress { ServerName = server.Name, Status = "Running", ProgressPercent = 10, Message = "Connecting" });

            await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    var winscpAssembly = LoadWinScpAssembly(config.WinScpAssemblyPath);
                    var sessionOptions = BuildSessionOptions(winscpAssembly, server, config, passphrase);
                    using var session = CreateSession(winscpAssembly);
                    OpenSession(session, sessionOptions);
                    logger.Add($"[{DateTime.Now:HH:mm:ss}] Connected to server via WinSCP .NET assembly");

                    if (configOnly)
                    {
                        progress?.Report(new ServerBackupProgress { ServerName = server.Name, Status = "Running", ProgressPercent = 60, Message = "Downloading config" });
                        var remoteConfig = server.RemoteConfigPath.Replace('\\', '/');
                        var localConfig = Path.Combine(serverBase, Path.GetFileName(remoteConfig));
                        DownloadFile(session, remoteConfig, localConfig);
                        logger.Add($"[{DateTime.Now:HH:mm:ss}] Config download complete: {localConfig}");
                        return;
                    }

                    var totalDataBytes = EstimateRemoteDataSizeBytes(session, server.RemoteDataPath);
                    using var transferSubscription = AttachTransferProgressHandler(session, server.Name, totalDataBytes, progress);

                    progress?.Report(new ServerBackupProgress { ServerName = server.Name, Status = "Running", ProgressPercent = 35, Message = "Synchronizing data" });
                    SynchronizeDirectory(session, server.RemoteDataPath, dataFolder);
                    logger.Add($"[{DateTime.Now:HH:mm:ss}] Data sync complete");

                    progress?.Report(new ServerBackupProgress { ServerName = server.Name, Status = "Running", ProgressPercent = 55, Message = "Downloading config" });
                    var remoteConfig2 = server.RemoteConfigPath.Replace('\\', '/');
                    var localConfig2 = Path.Combine(serverBase, Path.GetFileName(remoteConfig2));
                    DownloadFile(session, remoteConfig2, localConfig2);
                    logger.Add($"[{DateTime.Now:HH:mm:ss}] Config download complete: {localConfig2}");
                }
                catch (Exception ex) when (ShouldFallbackToCli(ex))
                {
                    logger.Add($"[{DateTime.Now:HH:mm:ss}] WinSCP .NET assembly incompatible on this runtime. Falling back to WinSCP CLI.");

                    progress?.Report(new ServerBackupProgress { ServerName = server.Name, Status = "Running", ProgressPercent = 30, Message = "Using WinSCP CLI fallback" });
                    RunWinScpCliTransfer(server, config, passphrase, dataFolder, serverBase, configOnly, logger, progress, cancellationToken);
                    logger.Add($"[{DateTime.Now:HH:mm:ss}] Connected and transferred via WinSCP CLI fallback");
                }
            }, cancellationToken);

            if (configOnly)
            {
                logger.Add($"[{DateTime.Now:HH:mm:ss}] Config-only backup completed.");
                progress?.Report(new ServerBackupProgress { ServerName = server.Name, Status = "Success", ProgressPercent = 100, Message = "Completed (config only)" });
                return;
            }

            progress?.Report(new ServerBackupProgress { ServerName = server.Name, Status = "Running", ProgressPercent = 75, Message = "Creating selective zip" });
            var zippedAny = CreateSelectiveZip(dataFolder, zipPath, logger, server.Name, progress);

            progress?.Report(new ServerBackupProgress { ServerName = server.Name, Status = "Running", ProgressPercent = 85, Message = "Cleaning temporary data" });
            CleanDataFolder(dataFolder);

            progress?.Report(new ServerBackupProgress { ServerName = server.Name, Status = "Running", ProgressPercent = 92, Message = "Applying retention" });
            EnforceRetention(serverBase, config.RetentionCount, logger);

            logger.Add($"[{DateTime.Now:HH:mm:ss}] Backup completed. Zip created: {(zippedAny ? zipPath : "No matching 01* folders")}");
            progress?.Report(new ServerBackupProgress { ServerName = server.Name, Status = "Success", ProgressPercent = 100, Message = "Completed" });
        }
        catch (Exception ex)
        {
            var detailedError = BuildDetailedErrorMessage(ex);
            logger.Add($"[{DateTime.Now:HH:mm:ss}] ERROR: {detailedError}");
            progress?.Report(new ServerBackupProgress { ServerName = server.Name, Status = "Failed", ProgressPercent = 100, Message = detailedError });
            throw new InvalidOperationException(detailedError, ex);
        }
        finally
        {
            await File.AppendAllLinesAsync(logFile, logger, cancellationToken);
        }
    }

    private static object BuildSessionOptions(Assembly winscpAssembly, ServerConfig server, AppConfig config, string passphrase)
    {
        var sessionOptionsType = winscpAssembly.GetType("WinSCP.SessionOptions")
            ?? throw new InvalidOperationException("WinSCP.SessionOptions type not found.");
        var protocolType = winscpAssembly.GetType("WinSCP.Protocol")
            ?? throw new InvalidOperationException("WinSCP.Protocol enum not found.");

        var sessionOptions = Activator.CreateInstance(sessionOptionsType)
            ?? throw new InvalidOperationException("Failed to create WinSCP.SessionOptions.");

        sessionOptionsType.GetProperty("Protocol")?.SetValue(sessionOptions, Enum.Parse(protocolType, "Sftp"));
        sessionOptionsType.GetProperty("HostName")?.SetValue(sessionOptions, server.IpAddress);
        sessionOptionsType.GetProperty("UserName")?.SetValue(sessionOptions, "root");
        sessionOptionsType.GetProperty("SshPrivateKeyPath")?.SetValue(sessionOptions, config.PrivateKeyPath);
        sessionOptionsType.GetProperty("PrivateKeyPassphrase")?.SetValue(sessionOptions, passphrase);
        sessionOptionsType.GetProperty("GiveUpSecurityAndAcceptAnySshHostKey")?.SetValue(sessionOptions, true);

        return sessionOptions;
    }

    private static dynamic CreateSession(Assembly winscpAssembly)
    {
        var sessionType = winscpAssembly.GetType("WinSCP.Session")
            ?? throw new InvalidOperationException("WinSCP.Session type not found.");

        return Activator.CreateInstance(sessionType)
            ?? throw new InvalidOperationException("Failed to create WinSCP.Session.");
    }

    private static void OpenSession(dynamic session, object sessionOptions)
    {
        var sessionType = session.GetType();
        var openMethod = sessionType.GetMethod("Open", new[] { sessionOptions.GetType() })
            ?? throw new InvalidOperationException("WinSCP.Session.Open(SessionOptions) method not found.");

        InvokeAndUnwrap(openMethod, session, new[] { sessionOptions });
    }

    private static void SynchronizeDirectory(dynamic session, string remotePath, string localPath)
    {
        var assembly = session.GetType().Assembly;
        var syncModeType = assembly.GetType("WinSCP.SynchronizationMode")
            ?? throw new InvalidOperationException("WinSCP.SynchronizationMode type not found.");
        var syncMode = Enum.Parse(syncModeType, "Local");

        var syncMethod = session.GetType().GetMethod(
            "SynchronizeDirectories",
            new[] { syncModeType, typeof(string), typeof(string), typeof(bool) })
            ?? throw new InvalidOperationException("WinSCP.Session.SynchronizeDirectories method not found.");

        var result = InvokeAndUnwrap(syncMethod, session, new[] { syncMode, localPath, remotePath, false })
            ?? throw new InvalidOperationException("SynchronizeDirectories returned null result.");

        var checkMethod = result.GetType().GetMethod("Check")
            ?? throw new InvalidOperationException("SynchronizationResult.Check method not found.");
        InvokeAndUnwrap(checkMethod, result, null);
    }

    private static void DownloadFile(dynamic session, string remotePath, string localPath)
    {
        var getFilesMethod = session.GetType().GetMethod(
            "GetFiles",
            new[] { typeof(string), typeof(string), typeof(bool) })
            ?? throw new InvalidOperationException("WinSCP.Session.GetFiles(remote, local, remove) method not found.");

        var result = InvokeAndUnwrap(getFilesMethod, session, new object[] { remotePath, localPath, false })
            ?? throw new InvalidOperationException("GetFiles returned null result.");

        var checkMethod = result.GetType().GetMethod("Check")
            ?? throw new InvalidOperationException("TransferOperationResult.Check method not found.");
        InvokeAndUnwrap(checkMethod, result, null);
    }

    private static bool CreateSelectiveZip(
        string dataFolder,
        string zipPath,
        List<string> logger,
        string serverName,
        IProgress<ServerBackupProgress>? progress)
    {
        if (File.Exists(zipPath))
        {
            File.Delete(zipPath);
        }

        var matches = Directory.GetDirectories(dataFolder, "01*");
        if (matches.Length == 0)
        {
            logger.Add($"[{DateTime.Now:HH:mm:ss}] No directories starting with 01* found. Skipping zip creation.");
            return false;
        }

        using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        var files = matches
            .SelectMany(folder => Directory.GetFiles(folder, "*", SearchOption.AllDirectories))
            .ToList();
        var total = Math.Max(1, files.Count);
        var processed = 0;

        foreach (var folder in matches)
        {
            var rootName = Path.GetFileName(folder);
            foreach (var file in Directory.GetFiles(folder, "*", SearchOption.AllDirectories))
            {
                var relativePath = Path.GetRelativePath(folder, file);
                var entryName = Path.Combine(rootName, relativePath).Replace('\\', '/');
                zip.CreateEntryFromFile(file, entryName, CompressionLevel.Optimal);

                processed++;
                var phaseProgress = 72 + (int)Math.Round((processed / (double)total) * 12);
                progress?.Report(new ServerBackupProgress
                {
                    ServerName = serverName,
                    Status = "Running",
                    ProgressPercent = Math.Clamp(phaseProgress, 72, 84),
                    Message = "Creating selective zip"
                });
            }
        }

        logger.Add($"[{DateTime.Now:HH:mm:ss}] Created zip: {zipPath}");
        return true;
    }

    private static void CleanDataFolder(string dataFolder)
    {
        if (!Directory.Exists(dataFolder))
        {
            return;
        }

        foreach (var entry in Directory.EnumerateFileSystemEntries(dataFolder))
        {
            if (Directory.Exists(entry))
            {
                Directory.Delete(entry, true);
            }
            else
            {
                File.Delete(entry);
            }
        }
    }

    private static void EnforceRetention(string serverBaseFolder, int retentionCount, List<string> logger)
    {
        var effectiveRetention = Math.Max(1, retentionCount);
        var zipFiles = Directory.GetFiles(serverBaseFolder, "data_*.zip")
            .Select(path => new FileInfo(path))
            .OrderByDescending(info => info.CreationTimeUtc)
            .ToList();

        foreach (var staleFile in zipFiles.Skip(effectiveRetention))
        {
            staleFile.Delete();
            logger.Add($"[{DateTime.Now:HH:mm:ss}] Deleted old archive: {staleFile.Name}");
        }
    }

    private static Assembly LoadWinScpAssembly(string assemblyPath)
    {
        if (string.IsNullOrWhiteSpace(assemblyPath) || !File.Exists(assemblyPath))
        {
            throw new FileNotFoundException("WinSCPnet.dll path is invalid. Update it in Configuration.", assemblyPath);
        }

        return Assembly.LoadFrom(assemblyPath);
    }

    private static object? InvokeAndUnwrap(MethodInfo method, object target, object?[]? args)
    {
        try
        {
            return method.Invoke(target, args);
        }
        catch (TargetInvocationException tie) when (tie.InnerException is not null)
        {
            throw new InvalidOperationException(tie.InnerException.Message, tie.InnerException);
        }
    }

    private static string BuildDetailedErrorMessage(Exception ex)
    {
        var sb = new StringBuilder();
        var current = ex;

        while (current is not null)
        {
            if (!string.IsNullOrWhiteSpace(current.Message))
            {
                if (sb.Length > 0)
                {
                    sb.Append(" | ");
                }

                sb.Append(current.Message);
            }

            current = current.InnerException!;
        }

        return sb.Length == 0 ? "Unknown error" : sb.ToString();
    }

    private static bool ShouldFallbackToCli(Exception ex)
    {
        var detail = BuildDetailedErrorMessage(ex);
        return detail.Contains("EventWaitHandle..ctor", StringComparison.OrdinalIgnoreCase)
            || detail.Contains("System.Threading.EventWaitHandle", StringComparison.OrdinalIgnoreCase)
            || ex is MissingMethodException;
    }

    private static void RunWinScpCliTransfer(
        ServerConfig server,
        AppConfig config,
        string passphrase,
        string dataFolder,
        string serverBase,
        bool configOnly,
        List<string> logger,
        IProgress<ServerBackupProgress>? progress,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var cliPath = ResolveWinScpCliPath(config.WinScpAssemblyPath);
        var remoteData = server.RemoteDataPath.Replace('\\', '/');
        var remoteConfig = server.RemoteConfigPath.Replace('\\', '/');
        var localConfig = Path.Combine(serverBase, Path.GetFileName(remoteConfig));
        var remoteTotalBytes = configOnly ? null : TryGetRemoteDirectorySizeViaCli(cliPath, server, config, passphrase);

        if (!configOnly)
        {
            Directory.CreateDirectory(dataFolder);
        }

        var scriptPath = Path.Combine(Path.GetTempPath(), $"winscp_{Guid.NewGuid():N}.txt");
        try
        {
            var scriptLines = new[]
            {
                "option batch abort",
                "option confirm off",
                $"open sftp://root@{EscapeWinScpArg(server.IpAddress)}/ -privatekey=\"{EscapeWinScpArg(config.PrivateKeyPath)}\" -passphrase=\"{EscapeWinScpArg(passphrase)}\" -hostkey=\"*\"",
                configOnly ? string.Empty : $"synchronize local \"{EscapeWinScpArg(dataFolder)}\" \"{EscapeWinScpArg(remoteData)}\"",
                $"get \"{EscapeWinScpArg(remoteConfig)}\" \"{EscapeWinScpArg(localConfig)}\"",
                "exit"
            }.Where(line => !string.IsNullOrWhiteSpace(line)).ToArray();

            File.WriteAllLines(scriptPath, scriptLines);

            var psi = new ProcessStartInfo
            {
                FileName = cliPath,
                Arguments = $"/ini=nul /script=\"{scriptPath}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi)
                ?? throw new InvalidOperationException("Failed to start WinSCP CLI process.");

            var stdOutBuilder = new StringBuilder();
            var stdErrBuilder = new StringBuilder();

            process.OutputDataReceived += (_, args) =>
            {
                if (string.IsNullOrWhiteSpace(args.Data))
                {
                    return;
                }

                stdOutBuilder.AppendLine(args.Data);

                if (!configOnly && TryParseCliTransferLine(args.Data, out var fileName, out var filePercent))
                {
                    progress?.Report(new ServerBackupProgress
                    {
                        ServerName = server.Name,
                        Status = "Running",
                        ProgressPercent = 0,
                        Message = $"Backing up {fileName} - {filePercent}%"
                    });
                }

                var transferPercent = TryExtractPercent(args.Data);
                if (transferPercent.HasValue)
                {
                    var mapped = configOnly
                        ? 60 + (int)Math.Round((transferPercent.Value / 100d) * 35)
                        : 10 + (int)Math.Round((transferPercent.Value / 100d) * 60);
                    long? transferredBytes = null;
                    if (remoteTotalBytes.HasValue)
                    {
                        transferredBytes = (long)Math.Round(remoteTotalBytes.Value * (transferPercent.Value / 100d));
                    }

                    progress?.Report(new ServerBackupProgress
                    {
                        ServerName = server.Name,
                        Status = "Running",
                        ProgressPercent = configOnly ? Math.Clamp(mapped, 60, 95) : Math.Clamp(mapped, 10, 70),
                        Message = configOnly ? "Downloading config" : "Transferring data",
                        TransferredBytes = transferredBytes,
                        TotalBytes = remoteTotalBytes
                    });
                }
            };

            process.ErrorDataReceived += (_, args) =>
            {
                if (!string.IsNullOrWhiteSpace(args.Data))
                {
                    stdErrBuilder.AppendLine(args.Data);
                }
            };

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            while (!process.WaitForExit(250))
            {
                if (!configOnly && remoteTotalBytes.HasValue && remoteTotalBytes.Value > 0)
                {
                    var downloadedBytes = GetDirectorySizeBytes(dataFolder);
                    var normalized = Math.Clamp(downloadedBytes / (double)remoteTotalBytes.Value, 0d, 1d);
                    var mapped = 10 + (int)Math.Round(normalized * 60d);
                    progress?.Report(new ServerBackupProgress
                    {
                        ServerName = server.Name,
                        Status = "Running",
                        ProgressPercent = Math.Clamp(mapped, 10, 70),
                        Message = "Transferring data",
                        TransferredBytes = downloadedBytes,
                        TotalBytes = remoteTotalBytes
                    });
                }

                if (!cancellationToken.IsCancellationRequested)
                {
                    continue;
                }

                try
                {
                    process.Kill(true);
                }
                catch
                {
                    // Ignore kill failures if process already exited.
                }

                throw new OperationCanceledException(cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();

            var stdOut = stdOutBuilder.ToString();
            var stdErr = stdErrBuilder.ToString();

            if (process.ExitCode != 0)
            {
                var output = string.IsNullOrWhiteSpace(stdErr) ? stdOut : stdErr;
                throw new InvalidOperationException($"WinSCP CLI failed (exit code {process.ExitCode}): {output.Trim()}");
            }

            if (!string.IsNullOrWhiteSpace(stdOut))
            {
                logger.Add($"[{DateTime.Now:HH:mm:ss}] WinSCP CLI output: {stdOut.Trim()}");
            }
        }
        finally
        {
            if (File.Exists(scriptPath))
            {
                File.Delete(scriptPath);
            }
        }
    }

    private static string ResolveWinScpCliPath(string configuredPath)
    {
        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            throw new FileNotFoundException("WinSCP path is empty. Set WinSCPnet.dll path or winscp.com path in Configuration.");
        }

        if (File.Exists(configuredPath) && configuredPath.EndsWith("winscp.com", StringComparison.OrdinalIgnoreCase))
        {
            return configuredPath;
        }

        var directory = File.Exists(configuredPath)
            ? Path.GetDirectoryName(configuredPath)
            : configuredPath;

        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            throw new FileNotFoundException("Could not resolve WinSCP installation directory from configured path.", configuredPath);
        }

        var comPath = Path.Combine(directory, "WinSCP.com");
        if (File.Exists(comPath))
        {
            return comPath;
        }

        var exePath = Path.Combine(directory, "WinSCP.exe");
        if (File.Exists(exePath))
        {
            return exePath;
        }

        throw new FileNotFoundException("Could not find WinSCP.com or WinSCP.exe near configured WinSCP path.", configuredPath);
    }

    private static string EscapeWinScpArg(string value)
    {
        return value.Replace("\"", "\"\"");
    }

    private static long GetDirectorySizeBytes(string rootPath)
    {
        if (!Directory.Exists(rootPath))
        {
            return 0;
        }

        long total = 0;
        foreach (var file in Directory.EnumerateFiles(rootPath, "*", SearchOption.AllDirectories))
        {
            try
            {
                total += new FileInfo(file).Length;
            }
            catch
            {
                // Ignore files that disappear mid-scan.
            }
        }

        return total;
    }

    private static IDisposable? AttachTransferProgressHandler(dynamic session, string serverName, long totalBytes, IProgress<ServerBackupProgress>? progress)
    {
        if (progress is null)
        {
            return null;
        }

        var eventInfo = session.GetType().GetEvent("FileTransferProgress");
        if (eventInfo is null || eventInfo.EventHandlerType is null)
        {
            return null;
        }

        var bridge = new TransferProgressBridge(serverName, totalBytes, progress);
        var handlerMethod = typeof(TransferProgressBridge).GetMethod(nameof(TransferProgressBridge.OnProgress))
            ?? throw new InvalidOperationException("Transfer progress handler method not found.");
        var handler = Delegate.CreateDelegate(eventInfo.EventHandlerType, bridge, handlerMethod, false);

        if (handler is null)
        {
            return null;
        }

        eventInfo.AddEventHandler(session, handler);
        return new DelegateSubscription(() => eventInfo.RemoveEventHandler(session, handler));
    }

    private static long EstimateRemoteDataSizeBytes(dynamic session, string remotePath)
    {
        try
        {
            var optionsType = session.GetType().Assembly.GetType("WinSCP.EnumerationOptions");
            var remoteInfoType = session.GetType().Assembly.GetType("WinSCP.RemoteFileInfo");
            if (optionsType is null || remoteInfoType is null)
            {
                return 0;
            }

            var allDirectories = Enum.Parse(optionsType, "AllDirectories");
            var method = session.GetType().GetMethod(
                "EnumerateRemoteFiles",
                new[] { typeof(string), typeof(string), optionsType });

            if (method is null)
            {
                return 0;
            }

            var enumerable = method.Invoke(session, new object?[] { remotePath, null, allDirectories }) as System.Collections.IEnumerable;
            if (enumerable is null)
            {
                return 0;
            }

            long total = 0;
            foreach (var item in enumerable)
            {
                if (item is null)
                {
                    continue;
                }

                var isDirectoryProp = remoteInfoType.GetProperty("IsDirectory");
                var lengthProp = remoteInfoType.GetProperty("Length");
                var isDir = (bool?)isDirectoryProp?.GetValue(item) ?? false;
                if (isDir)
                {
                    continue;
                }

                var lenValue = lengthProp?.GetValue(item);
                if (lenValue is long len)
                {
                    total += Math.Max(0, len);
                }
            }

            return total;
        }
        catch
        {
            return 0;
        }
    }

    private static long? TryGetRemoteDirectorySizeViaCli(string cliPath, ServerConfig server, AppConfig config, string passphrase)
    {
        var remoteData = server.RemoteDataPath.Replace('\\', '/');
        var scriptPath = Path.Combine(Path.GetTempPath(), $"winscp_size_{Guid.NewGuid():N}.txt");

        try
        {
            var scriptLines = new[]
            {
                "option batch abort",
                "option confirm off",
                $"open sftp://root@{EscapeWinScpArg(server.IpAddress)}/ -privatekey=\"{EscapeWinScpArg(config.PrivateKeyPath)}\" -passphrase=\"{EscapeWinScpArg(passphrase)}\" -hostkey=\"*\"",
                $"call du -sb \"{EscapeWinScpArg(remoteData)}\"",
                "exit"
            };

            File.WriteAllLines(scriptPath, scriptLines);

            var psi = new ProcessStartInfo
            {
                FileName = cliPath,
                Arguments = $"/ini=nul /script=\"{scriptPath}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process is null)
            {
                return null;
            }

            var output = process.StandardOutput.ReadToEnd() + Environment.NewLine + process.StandardError.ReadToEnd();
            process.WaitForExit();

            var match = Regex.Match(output, @"(?m)^(?<size>\d+)\s+");
            if (!match.Success)
            {
                return null;
            }

            return long.TryParse(match.Groups["size"].Value, out var size) ? size : null;
        }
        catch
        {
            return null;
        }
        finally
        {
            if (File.Exists(scriptPath))
            {
                File.Delete(scriptPath);
            }
        }
    }

    private static int? TryExtractPercent(string line)
    {
        var match = PercentRegex.Match(line);
        if (!match.Success)
        {
            return null;
        }

        if (!int.TryParse(match.Groups["pct"].Value, out var value))
        {
            return null;
        }

        return Math.Clamp(value, 0, 100);
    }

    private static bool TryParseCliTransferLine(string line, out string fileName, out int percent)
    {
        fileName = string.Empty;
        percent = 0;

        var match = CliTransferLineRegex.Match(line);
        if (!match.Success)
        {
            return false;
        }

        var rawName = match.Groups["name"].Value.Trim();
        if (string.IsNullOrWhiteSpace(rawName))
        {
            return false;
        }

        if (!int.TryParse(match.Groups["pct"].Value, out percent))
        {
            return false;
        }

        fileName = rawName;
        percent = Math.Clamp(percent, 0, 100);
        return true;
    }

    private sealed class DelegateSubscription : IDisposable
    {
        private readonly Action _dispose;
        private bool _disposed;

        public DelegateSubscription(Action dispose)
        {
            _dispose = dispose;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _dispose();
        }
    }

    private sealed class TransferProgressBridge
    {
        private readonly string _serverName;
        private readonly long _totalBytes;
        private readonly IProgress<ServerBackupProgress> _progress;
        private int _lastPercent = -1;
        private string _lastMessage = string.Empty;

        public TransferProgressBridge(string serverName, long totalBytes, IProgress<ServerBackupProgress> progress)
        {
            _serverName = serverName;
            _totalBytes = Math.Max(0, totalBytes);
            _progress = progress;
        }

        public void OnProgress(object sender, object eventArgs)
        {
            var raw = ReadNumericProperty(eventArgs, "OverallProgress");

            if (!raw.HasValue)
            {
                return;
            }

            var normalized = raw.Value > 1d ? raw.Value / 100d : raw.Value;
            normalized = Math.Clamp(normalized, 0d, 1d);

            var mapped = Math.Clamp(10 + (int)Math.Round(normalized * 60d), 10, 70);
            var fileName = ReadStringProperty(eventArgs, "FileName")
                ?? ReadStringProperty(eventArgs, "FilePath")
                ?? "file";
            var fileOnly = Path.GetFileName(fileName.Trim());
            var message = $"Backing up {fileOnly} - {(int)Math.Round(normalized * 100)}%";

            if (mapped == _lastPercent && string.Equals(message, _lastMessage, StringComparison.Ordinal))
            {
                return;
            }

            _lastPercent = mapped;
            _lastMessage = message;
            var transferredBytes = _totalBytes > 0
                ? (long)Math.Round(_totalBytes * normalized)
                : (long?)null;

            _progress.Report(new ServerBackupProgress
            {
                ServerName = _serverName,
                Status = "Running",
                ProgressPercent = mapped,
                Message = message,
                TransferredBytes = transferredBytes,
                TotalBytes = _totalBytes > 0 ? _totalBytes : null
            });
        }

        private static double? ReadNumericProperty(object target, string propertyName)
        {
            var property = target.GetType().GetProperty(propertyName);
            var value = property?.GetValue(target);

            return value switch
            {
                double d => d,
                float f => f,
                decimal m => (double)m,
                int i => i,
                long l => l,
                _ => null
            };
        }

        private static string? ReadStringProperty(object target, string propertyName)
        {
            var property = target.GetType().GetProperty(propertyName);
            return property?.GetValue(target) as string;
        }
    }
}
