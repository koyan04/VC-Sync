# VC Sync v2.2.0 - Installation Guide

## Quick Start

### Using the Installer (Recommended)

1. **Download** `VC-Sync-2.2.0-Installer.zip` from the [releases page](https://github.com/koyan04/VC-Sync/releases)
2. **Extract** the ZIP file to a temporary location
3. **Run** `Setup.bat` - Windows will prompt for administrator privileges
4. **Follow** the installation wizard
5. **Launch** VC Sync from the Start Menu or Desktop

### Installation Options

The installer will:
- Create an application folder in `C:\Program Files\VC Sync`
- Create shortcuts in Start Menu and Desktop
- Register the application in Add/Remove Programs
- Automatically launch VC Sync after installation

You can customize the installation path by opening a command prompt as Administrator and running:
```cmd
powershell -ExecutionPolicy Bypass -File ".\Install-VCSync.ps1" -InstallPath "D:\MyApps\VC Sync"
```

## Portable Version

If you prefer not to use the installer, download `VC-Sync-Setup-2.2.0.zip` which contains just the application binaries. Extract and run `VCSyncBackupApp.exe` directly.

## System Requirements

- **Windows 10** or later (64-bit)
- **.NET Runtime 8.0** or later
  - If not installed, Windows will prompt you to install it
- **Administrator privileges** (for installation only)
- **SSH/SFTP capability** for remote server operations

## Uninstallation

### Using the Uninstaller

Run `Uninstall-VCSync.bat` from the installation directory, or use Windows **Add/Remove Programs**:
1. Open Settings → Apps → Apps & features
2. Search for "VC Sync"
3. Click and select "Uninstall"

### For Portable Version

Simply delete the extracted folder.

## First Run

On first launch, VC Sync will:
1. Create a configuration file at `%APPDATA%\VC Sync\config.json`
2. Initialize the application data directory
3. Prompt you to add your first backup server

## Troubleshooting

### "Administrator privileges required"
- Right-click `Setup.bat` and select "Run as Administrator"

### ".NET Runtime 8.0 not found"
- Download from [Microsoft .NET website](https://dotnet.microsoft.com/download/dotnet/8.0)
- Or allow Windows to install it when prompted

### Installation fails
- Ensure you have administrator privileges
- Disable antivirus temporarily during installation
- Try extracting to a different location
- Check that you have sufficient disk space

### Application won't start
- Verify .NET 8.0 Runtime is installed
- Check the application log in `%APPDATA%\VC Sync\logs\`
- Run the application from command line to see error messages

## Features

- **Dual Config-Only Workflows**: Backup server configuration with or without data archives
- **Themed Interface**: Consistent dark theme throughout the application
- **Secure Storage**: Encrypted passphrase storage using Windows DPAPI
- **Server Management**: Add, edit, and manage multiple backup servers
- **Selective Backup**: Choose which components to backup (config only, data only, or both)
- **Retention Policy**: Automatic cleanup of old backups
- **Import/Export**: Save and restore server configurations

## Support

For issues or feature requests, visit [GitHub Issues](https://github.com/koyan04/VC-Sync/issues)

## License

See [LICENSE](../LICENSE) file in the repository
