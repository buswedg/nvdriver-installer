# ==============================================================================
# NVDriver Installer - Multi-Phase Workflow Resume Handler
# ==============================================================================
# This script handles automated resumption in both Safe Mode (DDU Cleanup)
# and Normal Mode (Driver Setup & Finalization), providing full interactive
# PowerShell console visibility throughout the entire process.
# ==============================================================================

[CmdletBinding()]
param(
    [string]$PhaseOverride = ""
)

# 1. Resolve Application Directory (script is located in scripts/ under application root)
$scriptDir = $PSScriptRoot
if (-not $scriptDir) {
    $scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
}
$appDir = $scriptDir
if (-not (Test-Path (Join-Path $appDir "NVDriverInstaller.exe"))) {
    $parentDir = Split-Path -Parent $scriptDir
    if (Test-Path (Join-Path $parentDir "NVDriverInstaller.exe")) {
        $appDir = $parentDir
    }
}
$exePath = Join-Path $appDir "NVDriverInstaller.exe"

if (-not (Test-Path $exePath)) {
    Write-Host "[!] Fatal Error: NVDriverInstaller.exe not found at: $exePath" -ForegroundColor Red
    Read-Host "Press Enter to exit..."
    exit 1
}

# 2. Verify / Ensure Elevation
$currentPrincipal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
$isAdmin = $currentPrincipal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)

if (-not $isAdmin) {
    Write-Host "[*] Requesting Administrator privileges to resume workflow..." -ForegroundColor Yellow
    $elevatedArgs = "-NoProfile -ExecutionPolicy Bypass -WindowStyle Normal -File `"$($MyInvocation.MyCommand.Path)`""
    if ($PhaseOverride) { $elevatedArgs += " -PhaseOverride `"$PhaseOverride`"" }
    
    try {
        $elevatedProc = Start-Process -FilePath "powershell.exe" -ArgumentList $elevatedArgs -Verb RunAs -PassThru
        $elevatedProc.WaitForExit()
        exit $elevatedProc.ExitCode
    } catch {
        Write-Host "[!] Administrator elevation was denied or failed." -ForegroundColor Red
        Read-Host "Press Enter to exit..."
        exit 1
    }
}

# 3. Lock Working Directory
Set-Location -LiteralPath $appDir

# 4. Detect Safe Mode vs Normal Mode
$isSafeMode = $false
try {
    Add-Type -AssemblyName System.Windows.Forms -ErrorAction SilentlyContinue
    $bootMode = [System.Windows.Forms.SystemInformation]::BootMode
    if ($bootMode -ne [System.Windows.Forms.BootMode]::Normal) {
        $isSafeMode = $true
    }
} catch {
    if ($env:SAFEBOOT_OPTION) { $isSafeMode = $true }
}

# Also inspect workflow_state.json if available
$stateFile = Join-Path $appDir "workflow_state.json"
if (Test-Path $stateFile) {
    try {
        $rawJson = Get-Content -Path $stateFile -Raw | ConvertFrom-Json
        if ($rawJson.phase -eq "Prepared" -or $rawJson.phase -eq 1) {
            if ($bootMode -ne [System.Windows.Forms.BootMode]::Normal -or $env:SAFEBOOT_OPTION) {
                $isSafeMode = $true
            }
        } elseif ($rawJson.phase -eq "SafeModeDdu" -or $rawJson.phase -eq 2) {
            $isSafeMode = $false
        }
    } catch { }
}

if ($PhaseOverride -eq "safemode") { $isSafeMode = $true }
if ($PhaseOverride -eq "normal") { $isSafeMode = $false }

# 5. Display Banner & Status
Clear-Host
if ($isSafeMode) {
    $host.UI.RawUI.WindowTitle = "NVDriver Installer - Safe Mode DDU Cleanup"
    Write-Host "================================================================" -ForegroundColor Cyan
    Write-Host "         NVDriver Installer - Safe Mode DDU Cleanup             " -ForegroundColor Cyan
    Write-Host "================================================================" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "  Operating Mode:  Windows Safe Mode" -ForegroundColor Yellow
    Write-Host "  Phase:           Display Driver Uninstaller (DDU) Purge" -ForegroundColor Yellow
    Write-Host "  Application:     $appDir" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "----------------------------------------------------------------" -ForegroundColor DarkGray
    Write-Host "  The driver cleanup process is running. Please do not close    " -ForegroundColor DarkGray
    Write-Host "  this window. Display may flicker during cleanup.              " -ForegroundColor DarkGray
    Write-Host "----------------------------------------------------------------" -ForegroundColor DarkGray
    Write-Host ""
} else {
    $host.UI.RawUI.WindowTitle = "NVDriver Installer - Driver Installation"
    Write-Host "================================================================" -ForegroundColor Cyan
    Write-Host "         NVDriver Installer - Driver Installation Phase         " -ForegroundColor Cyan
    Write-Host "================================================================" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "  Operating Mode:  Normal Windows Session" -ForegroundColor Yellow
    Write-Host "  Phase:           Driver Package Setup & System Finalization" -ForegroundColor Yellow
    Write-Host "  Application:     $appDir" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "----------------------------------------------------------------" -ForegroundColor DarkGray
    Write-Host "  Installing clean NVIDIA drivers. If interactive setup wizard   " -ForegroundColor DarkGray
    Write-Host "  opens, proceed through the standard NVIDIA setup prompts.     " -ForegroundColor DarkGray
    Write-Host "----------------------------------------------------------------" -ForegroundColor DarkGray
    Write-Host ""
}

# 6. Execute NVDriverInstaller.exe --resume and stream output
& "$exePath" --resume | Out-Host
$exitCode = $LASTEXITCODE
if ($null -eq $exitCode) { $exitCode = 0 }

# 7. Post-Execution Handling
Write-Host ""
if ($exitCode -ne 0) {
    Write-Host "================================================================" -ForegroundColor Red
    Write-Host " [!] Operation completed with errors (Exit code: $exitCode)." -ForegroundColor Red
    Write-Host "================================================================" -ForegroundColor Red
    Write-Host ""
    Read-Host "Press Enter to close this window..."
    exit $exitCode
} else {
    Write-Host "================================================================" -ForegroundColor Green
    Write-Host " [+] Phase completed successfully!" -ForegroundColor Green
    Write-Host "================================================================" -ForegroundColor Green
    Write-Host ""
    if ($isSafeMode) {
        Write-Host "System will automatically reboot to Normal Mode shortly..." -ForegroundColor Yellow
        Start-Sleep -Seconds 5
    } else {
        Start-Sleep -Seconds 3
    }
    exit 0
}
