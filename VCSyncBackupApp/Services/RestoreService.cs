using System.Diagnostics;
using System.IO;
using System.Text;
using VCSyncBackupApp.Models;

namespace VCSyncBackupApp.Services;

public sealed class RestoreService
{
    public IReadOnlyList<string> BuildRestoreScriptLines(RestoreRequest request, bool maskSensitiveValues = false)
    {
        var destination = NormalizeRemoteDirectory(request.DestinationPath);

        var prometheusDataPath = $"{destination}prometheus/data";
        var newDataPath = $"{destination}new-data";
        var oldDataPath = $"{destination}old-data";
        var dataZipPath = $"{destination}data.zip";
        var configFileName = string.IsNullOrWhiteSpace(request.ConfigFilePath)
            ? "shadowbox_config.json"
            : Path.GetFileName(request.ConfigFilePath.Trim());
        var remoteConfigPath = $"{destination}{EscapeWinScpArg(configFileName)}";

        var passphraseValue = maskSensitiveValues
            ? "********"
            : EscapeWinScpArg(request.Passphrase);
        var configFilePath = string.IsNullOrWhiteSpace(request.ConfigFilePath)
            ? "<config-file-path-required>"
            : EscapeWinScpArg(request.ConfigFilePath.Trim());
        var dataZipFilePath = string.IsNullOrWhiteSpace(request.DataZipFilePath)
            ? "<data-zip-path-required>"
            : EscapeWinScpArg(request.DataZipFilePath.Trim());

        if (request.ConfigOnly)
        {
            return new[]
            {
                "option batch abort",
                "option confirm off",
                $"open sftp://root@{EscapeWinScpArg(request.ServerIpAddress)}/ -privatekey=\"{EscapeWinScpArg(request.PrivateKeyPath)}\" -passphrase=\"{passphraseValue}\" -hostkey=\"*\"",
                "echo ==== Upload configuration only ====",
                $"call rm -f \"{remoteConfigPath}\"",
                $"put \"{configFilePath}\" \"{remoteConfigPath}\"",
                "echo ==== Restart shadowbox ====",
                "call sh -c 'docker stop shadowbox >/dev/null 2>&1 || true'",
                "call docker start shadowbox",
                "echo ==== Config-only restore completed ====",
                "exit"
            };
        }

        return new[]
        {
            "option batch abort",
            "option confirm off",
            $"open sftp://root@{EscapeWinScpArg(request.ServerIpAddress)}/ -privatekey=\"{EscapeWinScpArg(request.PrivateKeyPath)}\" -passphrase=\"{passphraseValue}\" -hostkey=\"*\"",
            "echo ==== Install unzip ====",
            "call DEBIAN_FRONTEND=noninteractive apt-get update -y",
            "call DEBIAN_FRONTEND=noninteractive apt-get install -y unzip",
            "echo ==== Stop shadowbox before restore ====",
            "call sh -c 'docker stop shadowbox >/dev/null 2>&1 || true'",
            "echo ==== Prepare folders and backup old data ====",
            $"call mkdir -p \"{prometheusDataPath}\" \"{newDataPath}\" \"{oldDataPath}\"",
            $"call sh -c 'if [ -d \"{prometheusDataPath}\" ]; then cp -r \"{prometheusDataPath}/.\" \"{oldDataPath}/\"; fi'",
            $"call sh -c 'rm -rf \"{newDataPath}\"/*'",
            "echo ==== Upload config and data zip ====",
            $"call rm -f \"{remoteConfigPath}\" \"{dataZipPath}\"",
            $"put \"{configFilePath}\" \"{remoteConfigPath}\"",
            $"put \"{dataZipFilePath}\" \"{dataZipPath}\"",
            "echo ==== Extract backup data ====",
            $"call unzip -t \"{dataZipPath}\" >/dev/null",
            $"call unzip -o \"{dataZipPath}\" -d \"{newDataPath}/\"",
            "echo ==== Remove old data and runtime state ====",
            $"call sh -c 'rm -rf {prometheusDataPath}/01* \"{prometheusDataPath}/wal\" \"{prometheusDataPath}/chunks_head\" \"{prometheusDataPath}/queries.active\" \"{prometheusDataPath}/lock\"'",
            "echo ==== Restore new data to prometheus ====",
            $"call cp -r \"{newDataPath}/.\" \"{prometheusDataPath}/\"",
            "echo ==== Validate restored data ====",
            $"call sh -c 'ls -1 \"{prometheusDataPath}\"/01* >/dev/null 2>&1 || {{ echo \"No restored 01* data blocks were found in prometheus/data.\"; exit 1; }}'",
            "echo ==== Flush and start shadowbox ====",
            "call sync",
            "call docker start shadowbox",
            "echo ==== Validate shadowbox startup ====",
            "call sh -c 'for i in $(seq 1 30); do docker ps --format \"{{.Names}}\" | grep -qx shadowbox && exit 0; sleep 1; done; echo \"shadowbox container did not start\"; exit 1'",
            "echo ==== Cleanup ====",
            $"call rm -f \"{dataZipPath}\"",
            $"call sh -c 'rm -rf \"{newDataPath}\"/*'",
            "echo ==== Restore completed ====",
            "exit"
        };
    }

    public IReadOnlyList<string> BuildServerHardeningScriptLines(RestoreRequest request, bool maskSensitiveValues = false)
    {
        var passphraseValue = maskSensitiveValues
            ? "********"
            : EscapeWinScpArg(request.Passphrase);

        return new[]
        {
            "option batch abort",
            "option confirm off",
            $"open sftp://root@{EscapeWinScpArg(request.ServerIpAddress)}/ -privatekey=\"{EscapeWinScpArg(request.PrivateKeyPath)}\" -passphrase=\"{passphraseValue}\" -hostkey=\"*\"",
            "echo ==== Configure 2GB swap ====",
            "call sh -c 'if ! swapon --show=NAME --noheadings 2>/dev/null | grep -qx /swapfile; then if [ ! -f /swapfile ]; then fallocate -l 2G /swapfile 2>/dev/null || dd if=/dev/zero of=/swapfile bs=1M count=2048 status=none; chmod 600 /swapfile; mkswap /swapfile; fi; swapon /swapfile; fi'",
            "call sh -c 'if grep -q \"^/swapfile[[:space:]]\" /etc/fstab; then true; else echo \"/swapfile none swap sw 0 0\" >> /etc/fstab; fi'",
            "echo ==== Configure swappiness and BBR ====",
            "call sh -c 'if grep -q \"^vm.swappiness=\" /etc/sysctl.conf; then sed -i \"s/^vm.swappiness=.*/vm.swappiness=10/\" /etc/sysctl.conf; else echo \"vm.swappiness=10\" >> /etc/sysctl.conf; fi'",
            "call sh -c 'if grep -q \"^net.core.default_qdisc=\" /etc/sysctl.conf; then sed -i \"s/^net.core.default_qdisc=.*/net.core.default_qdisc=fq/\" /etc/sysctl.conf; else echo \"net.core.default_qdisc=fq\" >> /etc/sysctl.conf; fi'",
            "call sh -c 'if grep -q \"^net.ipv4.tcp_congestion_control=\" /etc/sysctl.conf; then sed -i \"s/^net.ipv4.tcp_congestion_control=.*/net.ipv4.tcp_congestion_control=bbr/\" /etc/sysctl.conf; else echo \"net.ipv4.tcp_congestion_control=bbr\" >> /etc/sysctl.conf; fi'",
            "call modprobe tcp_bbr || true",
            "call sysctl -w vm.swappiness=10",
            "call sysctl -w net.core.default_qdisc=fq",
            "call sh -c 'if sysctl -w net.ipv4.tcp_congestion_control=bbr >/dev/null 2>&1; then sysctl net.ipv4.tcp_congestion_control; else echo \"WARNING: BBR not supported by this kernel\"; fi'",
            "echo ==== Server hardening completed ====",
            "exit"
        };
    }

    public async Task RunServerRestoreAsync(
        RestoreRequest request,
        IProgress<string>? terminalOutput,
        CancellationToken cancellationToken)
    {
        ValidateLocalRestoreFiles(request);

        var cliPath = ResolveWinScpCliPath(request.WinScpAssemblyPath);
        var scriptPath = Path.Combine(Path.GetTempPath(), $"winscp_restore_{Guid.NewGuid():N}.txt");

        try
        {
            var scriptLines = BuildRestoreScriptLines(request, maskSensitiveValues: false);

            File.WriteAllLines(scriptPath, scriptLines);
            await RunWinScpScriptAsync(cliPath, scriptPath, terminalOutput, cancellationToken);
        }
        finally
        {
            if (File.Exists(scriptPath))
            {
                File.Delete(scriptPath);
            }
        }
    }

    public async Task RunServerHardeningAsync(
        RestoreRequest request,
        IProgress<string>? terminalOutput,
        CancellationToken cancellationToken)
    {
        var cliPath = ResolveWinScpCliPath(request.WinScpAssemblyPath);
        var scriptPath = Path.Combine(Path.GetTempPath(), $"winscp_hardening_{Guid.NewGuid():N}.txt");

        try
        {
            var scriptLines = BuildServerHardeningScriptLines(request, maskSensitiveValues: false);

            File.WriteAllLines(scriptPath, scriptLines);
            await RunWinScpScriptAsync(cliPath, scriptPath, terminalOutput, cancellationToken);
        }
        finally
        {
            if (File.Exists(scriptPath))
            {
                File.Delete(scriptPath);
            }
        }
    }

    private static async Task RunWinScpScriptAsync(
        string cliPath,
        string scriptPath,
        IProgress<string>? terminalOutput,
        CancellationToken cancellationToken)
    {
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
            ?? throw new InvalidOperationException("Failed to start WinSCP CLI restore process.");

        var outputBuilder = new StringBuilder();
        var errorBuilder = new StringBuilder();

        process.OutputDataReceived += (_, args) =>
        {
            if (string.IsNullOrWhiteSpace(args.Data))
            {
                return;
            }

            outputBuilder.AppendLine(args.Data);
            terminalOutput?.Report(args.Data);
        };

        process.ErrorDataReceived += (_, args) =>
        {
            if (string.IsNullOrWhiteSpace(args.Data))
            {
                return;
            }

            errorBuilder.AppendLine(args.Data);
            terminalOutput?.Report($"ERROR: {args.Data}");
        };

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(true);
                }
            }
            catch
            {
                // Ignore kill failures if process already exited.
            }

            throw;
        }

        process.WaitForExit();

        if (process.ExitCode == 0)
        {
            return;
        }

        var output = string.IsNullOrWhiteSpace(errorBuilder.ToString())
            ? outputBuilder.ToString()
            : errorBuilder.ToString();

        throw new InvalidOperationException($"Restore failed (exit code {process.ExitCode}): {output.Trim()}");
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

    private static string NormalizeRemoteDirectory(string destinationPath)
    {
        var normalized = destinationPath.Replace('\\', '/').Trim();
        if (!normalized.StartsWith('/'))
        {
            throw new InvalidOperationException("Destination path must be an absolute Linux path.");
        }

        if (!normalized.EndsWith('/'))
        {
            normalized += "/";
        }

        return normalized;
    }

    private static string EscapeWinScpArg(string value)
    {
        return value.Replace("\"", "\"\"");
    }

    private static void ValidateLocalRestoreFiles(RestoreRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ConfigFilePath))
        {
            throw new InvalidOperationException("Configuration JSON file path is required for restore.");
        }

        if (!File.Exists(request.ConfigFilePath))
        {
            throw new FileNotFoundException("Configuration JSON file was not found.", request.ConfigFilePath);
        }

        if (request.ConfigOnly)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(request.DataZipFilePath))
        {
            throw new InvalidOperationException("Data zip file path is required when config-only restore is disabled.");
        }

        if (!File.Exists(request.DataZipFilePath))
        {
            throw new FileNotFoundException("Data zip file was not found.", request.DataZipFilePath);
        }
    }
}
