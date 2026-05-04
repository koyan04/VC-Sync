# Building the Standalone Installer

## Option 1: Using Online Inno Setup Compiler (Recommended)

If you don't have Inno Setup installed, you can use the online compiler at:
https://www.innosetup.com/

However, an easier option is to download Inno Setup and compile it yourself.

## Option 2: Install Inno Setup and Compile

### Step 1: Download Inno Setup
1. Visit: https://www.innosetup.com/download.php
2. Download "Inno Setup 6.2.2 (or latest) - Full Installer"
3. Run the installer and follow the wizard

### Step 2: Compile the Installer Script
Once Inno Setup is installed, you can compile using:

#### Method A: Command Line
Open Command Prompt or PowerShell and run:
```cmd
"C:\Program Files (x86)\Inno Setup 6\ISCC.exe" "C:\path\to\VC-Sync-Setup.iss"
```

#### Method B: Using Inno Setup GUI
1. Open Inno Setup Compiler
2. File → Open (or press Ctrl+O)
3. Navigate to and select `VC-Sync-Setup.iss`
4. Click the Compile button (or press F9)
5. The installer will be created in `installer\dist\` as `VC-Sync-Setup-2.1.0.exe`

### Step 3: Distribute
The compiled `.exe` file (`VC-Sync-Setup-2.1.0.exe`) will be a standalone installer that users can run directly.

## Option 3: Use the Provided PowerShell Installer

If you cannot or don't want to use Inno Setup, the `VC-Sync-2.1.0-Installer.zip` includes a complete PowerShell-based installer that provides the same functionality:
- Setup.bat - Interactive setup launcher
- Install-VCSync.ps1 - Full installation script
- Uninstall-VCSync.bat - Uninstaller
- All application binaries

This is fully functional and doesn't require Inno Setup.

## What Gets Compiled?

The `VC-Sync-Setup.iss` Inno Setup script will create a professional Windows installer that:

1. **Installation**
   - Creates application directory in Program Files
   - Copies all application files
   - Creates Start Menu and Desktop shortcuts
   - Registers in Windows Add/Remove Programs
   - Provides uninstaller
   - Auto-launches on completion

2. **Uninstallation**
   - Complete removal of all files
   - Cleanup of shortcuts and registry entries
   - Preserves user settings if desired

3. **Features**
   - Modern Windows 10+ style wizard interface
   - 64-bit installation support
   - Customizable installation path
   - LZMA compression for smaller file size
   - Optional desktop shortcut creation

## Support

If you need help compiling the Inno Setup script:
- Ensure you have the correct Inno Setup version (6.2.2+)
- Verify the file paths in the .iss script match your system layout
- Check that all source files exist in the `publish\win-x64\` directory
- Contact support if compilation fails
