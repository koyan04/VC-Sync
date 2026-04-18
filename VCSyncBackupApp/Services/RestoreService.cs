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

        var passphraseValue = maskSensitiveValues
            ? "********"
            : EscapeWinScpArg(request.Passphrase);

        if (request.ConfigOnly)
        {
            return new[]
            {
                "option batch abort",
                "option confirm off",
                $"open sftp://root@{EscapeWinScpArg(request.ServerIpAddress)}/ -privatekey=\"{EscapeWinScpArg(request.PrivateKeyPath)}\" -passphrase=\"{passphraseValue}\" -hostkey=\"*\"",
                "echo ==== Upload configuration only ====",
                $"put -resume -rawtransfersettings PreserveTimeDirs=0 \"{EscapeWinScpArg(request.ConfigFilePath)}\" \"{destination}\"",
                "echo ==== Restart shadowbox ====",
                "call docker restart shadowbox",
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
            "echo ==== Prepare folders and backup old data ====",
            $"call mkdir -p \"{prometheusDataPath}\" \"{newDataPath}\" \"{oldDataPath}\"",
            $"call sh -c 'if [ -d \"{prometheusDataPath}\" ]; then cp -r \"{prometheusDataPath}/.\" \"{oldDataPath}/\"; fi'",
            "echo ==== Upload config and data zip ====",
            $"put -resume -rawtransfersettings PreserveTimeDirs=0 \"{EscapeWinScpArg(request.ConfigFilePath)}\" \"{destination}\"",
            $"put -resume -rawtransfersettings PreserveTimeDirs=0 \"{EscapeWinScpArg(request.DataZipFilePath)}\" \"{destination}\"",
            "echo ==== Rename and extract backup data ====",
            $"call sh -c 'mv {destination}data*.zip {dataZipPath}'",
            $"call unzip -o \"{dataZipPath}\" -d \"{newDataPath}/\"",
            "echo ==== Remove old 01* data folders ====",
            $"call sh -c 'rm -rf {prometheusDataPath}/01*'",
            "echo ==== Restore new data to prometheus ====",
            $"call cp -r \"{newDataPath}/.\" \"{prometheusDataPath}/\"",
            "echo ==== Cleanup and restart shadowbox ====",
            $"call rm -f \"{dataZipPath}\"",
            "call docker restart shadowbox",
            "echo ==== Restore completed ====",
            "exit"
        };
    }

    public async Task RunServerRestoreAsync(
        RestoreRequest request,
        IProgress<string>? terminalOutput,
        CancellationToken cancellationToken)
    {
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

        while (!process.WaitForExit(250))
        {
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

        await process.WaitForExitAsync(cancellationToken);

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
}
