# VC Sync Backup App

Windows desktop backup tool for Outline VPN servers with WinSCP .NET automation, selective zipping, retention, and per-server logs.

## Features

- Server management UI (add/edit/delete)
- JSON-based configuration persistence
- Secure passphrase storage using Windows DPAPI (current user scope)
- WinSCP .NET assembly integration (loaded from `WinSCPnet.dll` path)
- SFTP backup workflow:
  - sync remote data folder to local `data`
  - download remote config file
  - zip only folders matching `01*`
  - cleanup local `data` contents
  - enforce retention policy (`N` latest archives)
- Per-server logging in `logs/<ServerName>.log`
- Progress and status updates in UI
- Import/export server list JSON

## Requirements

- Windows 10/11
- .NET 8 SDK
- WinSCP .NET assembly (`WinSCPnet.dll`)
- SSH private key (`.ppk`) and passphrase

## Build

```powershell
dotnet build .\VCSyncBackupApp.sln
```

## Run

```powershell
dotnet run --project .\VCSyncBackupApp\VCSyncBackupApp.csproj
```

## First-Time Setup

1. Open the app.
2. Configure:
   - `WinSCPnet.dll` path (for example from your WinSCP installation)
   - Private key (`.ppk`) path
   - Passphrase
   - Base backup directory (default: `Desktop\VChannel\Premium`)
   - Retention count
3. Add one or more servers with name, IP, remote data path, and remote config path.
4. Click **Save Settings** and then **Backup All**.

## Output Structure

For each server:

```text
<base>\<ServerName>\
  data\                    (temporary sync folder; cleaned after zip)
  data_YYYY-MM-DD_HHMM.zip
  <downloaded config file>
<base>\logs\<ServerName>.log
```

## Notes

- Host key checking is intentionally relaxed via WinSCP session option equivalent to `hostkey="*"`.
- If no folders match `01*`, zip creation is skipped and logged.
- Passphrase is not stored in plain text.
