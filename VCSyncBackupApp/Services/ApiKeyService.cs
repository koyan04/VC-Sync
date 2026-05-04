using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json;

namespace VCSyncBackupApp.Services;

public sealed class ApiKeyService
{
    public async Task<string> GenerateApiKeyAsync(
        string winScpAssemblyPath,
        string privateKeyPath,
        string passphrase,
        string serverIpAddress,
        string sourceDirectory,
        CancellationToken cancellationToken)
    {
        var tempFilePath = Path.Combine(Path.GetTempPath(), $"vcsync_access_{Guid.NewGuid():N}.txt");
        var remoteAccessPath = BuildRemoteAccessPath(sourceDirectory);

        try
        {
            await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    // Try using WinSCP .NET assembly first
                    var winscpAssembly = LoadWinScpAssembly(winScpAssemblyPath);
                    var sessionOptions = BuildSessionOptions(winscpAssembly, serverIpAddress, privateKeyPath, passphrase);
                    using var session = CreateSession(winscpAssembly);
                    OpenSession(session, sessionOptions);
                    DownloadFile(session, remoteAccessPath, tempFilePath);
                }
                catch (Exception ex) when (ShouldFallbackToCli(ex))
                {
                    // Fall back to WinSCP CLI if .NET assembly fails
                    DownloadFileUsingCliAsync(winScpAssemblyPath, serverIpAddress, privateKeyPath, passphrase, remoteAccessPath, tempFilePath);
                }
            }, cancellationToken);

            var accessText = await File.ReadAllTextAsync(tempFilePath, cancellationToken);
            return BuildApiKeyJson(accessText, serverIpAddress);
        }
        finally
        {
            if (File.Exists(tempFilePath))
            {
                File.Delete(tempFilePath);
            }
        }
    }

    public static string BuildApiKeyJson(string accessText, string serverIpAddress)
    {
        var fields = ParseAccessFields(accessText);

        if (!fields.TryGetValue("certSha256", out var certSha256) || string.IsNullOrWhiteSpace(certSha256))
        {
            throw new InvalidOperationException("access.txt is missing certSha256.");
        }

        if (!fields.TryGetValue("apiUrl", out var apiUrl) || string.IsNullOrWhiteSpace(apiUrl))
        {
            throw new InvalidOperationException("access.txt is missing apiUrl.");
        }

        var normalizedApiUrl = ReplaceHost(apiUrl, serverIpAddress);

        return JsonSerializer.Serialize(new
        {
            apiUrl = normalizedApiUrl,
            certSha256
        });
    }

    private static Dictionary<string, string> ParseAccessFields(string accessText)
    {
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var rawLine in accessText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            var separatorIndex = line.IndexOf(':');
            if (separatorIndex <= 0)
            {
                continue;
            }

            var key = line[..separatorIndex].Trim();
            var value = line[(separatorIndex + 1)..].Trim();
            fields[key] = value;
        }

        return fields;
    }

    private static string ReplaceHost(string apiUrl, string serverIpAddress)
    {
        if (!Uri.TryCreate(apiUrl.Trim(), UriKind.Absolute, out var uri))
        {
            throw new InvalidOperationException("access.txt contains an invalid apiUrl.");
        }

        var builder = new UriBuilder(uri)
        {
            Host = serverIpAddress.Trim()
        };

        return builder.Uri.AbsoluteUri;
    }

    private static string BuildRemoteAccessPath(string sourceDirectory)
    {
        var normalizedDirectory = NormalizeRemoteDirectory(sourceDirectory);
        return $"{normalizedDirectory}access.txt";
    }

    private static string NormalizeRemoteDirectory(string remoteDirectory)
    {
        var normalized = (remoteDirectory ?? string.Empty).Trim().Replace('\\', '/');
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new InvalidOperationException("Source directory is required.");
        }

        if (!normalized.EndsWith('/'))
        {
            normalized += "/";
        }

        return normalized;
    }

    private static Assembly LoadWinScpAssembly(string assemblyPath)
    {
        if (!File.Exists(assemblyPath))
        {
            throw new InvalidOperationException("WinSCPnet.dll path is invalid.");
        }

        return Assembly.LoadFrom(assemblyPath);
    }

    private static object BuildSessionOptions(Assembly winscpAssembly, string serverIpAddress, string privateKeyPath, string passphrase)
    {
        var sessionOptionsType = winscpAssembly.GetType("WinSCP.SessionOptions")
            ?? throw new InvalidOperationException("WinSCP.SessionOptions type not found.");
        var protocolType = winscpAssembly.GetType("WinSCP.Protocol")
            ?? throw new InvalidOperationException("WinSCP.Protocol enum not found.");

        var sessionOptions = Activator.CreateInstance(sessionOptionsType)
            ?? throw new InvalidOperationException("Failed to create WinSCP.SessionOptions.");

        sessionOptionsType.GetProperty("Protocol")?.SetValue(sessionOptions, Enum.Parse(protocolType, "Sftp"));
        sessionOptionsType.GetProperty("HostName")?.SetValue(sessionOptions, serverIpAddress);
        sessionOptionsType.GetProperty("UserName")?.SetValue(sessionOptions, "root");
        sessionOptionsType.GetProperty("SshPrivateKeyPath")?.SetValue(sessionOptions, privateKeyPath);
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

    private static void DownloadFile(dynamic session, string remotePath, string localPath)
    {
        var getFilesMethod = session.GetType().GetMethod(
            "GetFiles",
            new[] { typeof(string), typeof(string), typeof(bool) })
            ?? throw new InvalidOperationException("WinSCP.Session.GetFiles(remote, local, remove) method not found.");

        var result = getFilesMethod.Invoke(session, new object[] { remotePath, localPath, false })
            ?? throw new InvalidOperationException("GetFiles returned null result.");

        var checkMethod = result.GetType().GetMethod("Check")
            ?? throw new InvalidOperationException("TransferOperationResult.Check method not found.");
        InvokeAndUnwrap(checkMethod, result, null);
    }

    private static object? InvokeAndUnwrap(MethodInfo method, object target, object?[]? arguments)
    {
        try
        {
            return method.Invoke(target, arguments);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            throw ex.InnerException;
        }
    }

    private static void DownloadFileUsingCliAsync(string configuredPath, string serverIpAddress, string privateKeyPath, string passphrase, string remoteFilePath, string localFilePath)
    {
        var cliPath = ResolveWinScpCliPath(configuredPath);
        var scriptPath = Path.Combine(Path.GetTempPath(), $"winscp_{Guid.NewGuid():N}.txt");

        try
        {
            var scriptLines = new[]
            {
                "option batch abort",
                "option confirm off",
                $"open sftp://root@{EscapeWinScpArg(serverIpAddress)}/ -privatekey=\"{EscapeWinScpArg(privateKeyPath)}\" -passphrase=\"{EscapeWinScpArg(passphrase)}\" -hostkey=\"*\"",
                $"get \"{EscapeWinScpArg(remoteFilePath)}\" \"{EscapeWinScpArg(localFilePath)}\"",
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

            using var process = Process.Start(psi)
                ?? throw new InvalidOperationException("Failed to start WinSCP CLI process.");

            var stdOut = new StringBuilder();
            var stdErr = new StringBuilder();

            process.OutputDataReceived += (_, args) =>
            {
                if (!string.IsNullOrWhiteSpace(args.Data))
                {
                    stdOut.AppendLine(args.Data);
                }
            };

            process.ErrorDataReceived += (_, args) =>
            {
                if (!string.IsNullOrWhiteSpace(args.Data))
                {
                    stdErr.AppendLine(args.Data);
                }
            };

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            process.WaitForExit();

            if (process.ExitCode != 0)
            {
                var output = stdErr.Length > 0 ? stdErr.ToString() : stdOut.ToString();
                throw new InvalidOperationException($"WinSCP CLI failed (exit code {process.ExitCode}): {output.Trim()}");
            }

            if (!File.Exists(localFilePath))
            {
                throw new InvalidOperationException($"WinSCP CLI did not create the expected output file: {localFilePath}");
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
}