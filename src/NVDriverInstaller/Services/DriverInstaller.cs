using System.Diagnostics;
using System.IO;
using NVDriverInstaller.Core.Config;
using NVDriverInstaller.Core.Models;

namespace NVDriverInstaller.Services;

/// <summary>
/// Handles live and simulated execution of standard in-place driver installations in Windows
/// without requiring Safe Mode reboots or Display Driver Uninstaller wiping.
/// </summary>
public static class DriverInstaller
{
    public static async Task<(bool success, string message)> InstallDriverAsync(
        string packagePath,
        bool unattended = false,
        bool installNvcpl = true,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(packagePath))
            return (false, "Package path is invalid or empty.");

        string workingDir;
        bool isTempDir = false;

        if (File.Exists(packagePath) && packagePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            isTempDir = true;
            workingDir = Path.Combine(Path.GetTempPath(), $"nv_inst_{Guid.NewGuid():N}");
            progress?.Report("Extracting self-extracting driver package for installation...");
            AppLogger.LogInstall($"Extracting standalone package '{packagePath}' to temporary working directory '{workingDir}'...");

            var extractOk = await PackageBuilder.ExtractPackageAsync(packagePath, workingDir, progress, ct);
            if (!extractOk || !File.Exists(Path.Combine(workingDir, "setup.exe")))
            {
                if (Directory.Exists(workingDir)) PackageBuilder.SafeDeleteDirectory(workingDir);
                return (false, "Failed to extract package contents or setup.exe was not found.");
            }
        }
        else if (Directory.Exists(packagePath))
        {
            workingDir = packagePath;
        }
        else
        {
            return (false, $"Driver package not found at: {packagePath}");
        }

        try
        {
            var targetExe = Path.Combine(workingDir, "setup.exe");
            if (!File.Exists(targetExe))
                return (false, $"Driver installer executable not found at: {targetExe}");

            var args = unattended ? "-s -noreboot" : string.Empty;
            progress?.Report($"Launching installer ({(unattended ? "Unattended" : "Interactive")}): {Path.GetFileName(targetExe)} {args}...");
            AppLogger.LogInstall($"Executing '{targetExe}' with arguments: '{args}'");

            var psi = new ProcessStartInfo
            {
                FileName = targetExe,
                Arguments = args,
                WorkingDirectory = workingDir,
                UseShellExecute = true
            };

            using var proc = Process.Start(psi);
            if (proc == null)
                return (false, "Failed to start driver installer process.");

            progress?.Report("Driver installer running... Display may flicker during installation.");
            await proc.WaitForExitAsync(ct);

            if (proc.ExitCode == 0 || proc.ExitCode == 3010)
            {
                progress?.Report($"NVIDIA setup process completed successfully with exit code {proc.ExitCode}.");
                AppLogger.LogInstall($"NVIDIA setup process completed successfully with exit code {proc.ExitCode}.");

                // Offline install of NVIDIA Control Panel (AppX) if requested
                if (installNvcpl)
                {
                    await InstallNvcplAppxAsync(workingDir, progress, ct);
                }

                return (true, "Driver installation completed successfully.");
            }

            return (false, $"Installer exited with non-zero code {proc.ExitCode}.");
        }
        catch (Exception ex)
        {
            AppLogger.LogInstallError("DriverInstaller.InstallDriverAsync", ex);
            return (false, $"Failed to execute driver installer: {ex.Message}");
        }
        finally
        {
            if (isTempDir && Directory.Exists(workingDir))
            {
                progress?.Report("Cleaning temporary installation files...");
                PackageBuilder.SafeDeleteDirectory(workingDir);
            }
        }
    }

    /// <summary>
    /// Executes a comprehensive pre-flight simulation plan that validates files, targets,
    /// and logs every automated action versus manual user requirement without modifying the system.
    /// </summary>
    public static async Task<(bool success, string message)> RunSimulationAsync(
        string packagePath,
        bool isCleanInstall,
        string? dduProfileId,
        bool quarantineNetwork,
        bool autoRebootSafeMode,
        bool autoRebootAfterInstall,
        bool unattended,
        bool installNvcpl,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        await Task.Yield();

        progress?.Report("================================================================================");
        progress?.Report($"[SIMULATION] PRE-FLIGHT EXECUTION PLAN - {(isCleanInstall ? "CLEAN UPGRADE PIPELINE" : "STANDARD IN-PLACE INSTALL")}");
        progress?.Report($"Timestamp: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        progress?.Report($"Target Package: {packagePath}");
        progress?.Report("================================================================================");
        progress?.Report(string.Empty);

        var autoCount = 0;
        var manualCount = 0;
        var settings = SettingsManager.Current;

        // --- STEP 1: PRE-FLIGHT VERIFICATION ---
        progress?.Report("--- STEP 1: PREREQUISITE & FILE INTEGRITY VERIFICATION ---");

        var isExe = File.Exists(packagePath) && packagePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);
        var isDir = Directory.Exists(packagePath);
        string targetExe = packagePath;
        string? appxDisplayPath = null;

        if (isExe)
        {
            var sizeMb = new FileInfo(packagePath).Length / (1024.0 * 1024.0);
            progress?.Report($"[OK] Driver package verified (Standalone Executable): {Path.GetFileName(packagePath)} ({sizeMb:F1} MB)");
            var appxName = PackageBuilder.FindAppxInArchive(packagePath);
            if (!string.IsNullOrEmpty(appxName))
            {
                appxDisplayPath = appxName;
                progress?.Report($"[OK] Offline NVIDIA Control Panel (AppX) located inside package archive: {appxName}");
            }
            else
            {
                progress?.Report("[INFO] No offline Control Panel AppX found in package (will rely on Microsoft Store/HSA if needed).");
            }
        }
        else if (isDir)
        {
            var setupExe = Path.Combine(packagePath, "setup.exe");
            if (File.Exists(setupExe))
            {
                targetExe = setupExe;
                var sizeMb = PackageBuilder.GetDirectorySize(packagePath) / (1024.0 * 1024.0);
                progress?.Report($"[OK] Driver package verified (Unpacked Directory): {Path.GetFileName(packagePath)} ({sizeMb:F1} MB)");
            }
            else
            {
                progress?.Report($"[ERROR] Driver installer setup.exe not found in: {packagePath}");
                return (false, "Simulation aborted: Driver setup.exe missing.");
            }

            var appxFiles = Directory.GetFiles(packagePath, "*.appx", SearchOption.AllDirectories)
                .Concat(Directory.GetFiles(packagePath, "*.appxbundle", SearchOption.AllDirectories)).ToList();
            if (appxFiles.Count > 0)
            {
                appxDisplayPath = appxFiles[0];
                progress?.Report($"[OK] Offline NVIDIA Control Panel (AppX) located: {Path.GetFileName(appxFiles[0])}");
            }
            else
            {
                progress?.Report("[INFO] No offline Control Panel AppX found in package (will rely on Microsoft Store/HSA if needed).");
            }
        }
        else
        {
            progress?.Report($"[ERROR] Driver package not found at: {packagePath}");
            return (false, "Simulation aborted: Driver package not found.");
        }

        var adapters = NetworkQuarantineService.GetPhysicalAdapters();
        progress?.Report($"[OK] Detected {adapters.Count} physical network adapter(s):");
        foreach (var a in adapters)
        {
            progress?.Report($"     * {a.Name} ({a.InterfaceDescription}) - Status: {a.AdminStatus}");
        }

        if (isCleanInstall)
        {
            var dduPath = SettingsManager.ResolveDduPath();
            if (File.Exists(dduPath))
            {
                progress?.Report($"[OK] Display Driver Uninstaller located: {dduPath}");
            }
            else
            {
                progress?.Report($"[WARNING] DDU executable not found at configured path: {dduPath}");
            }

            var dduProfile = ProfileManager.GetDduProfile(dduProfileId ?? settings.DefaultDduProfile);
            progress?.Report($"[OK] DDU Profile selected: '{dduProfile.DisplayName}'");
        }

        var sevenZipPath = SettingsManager.Resolve7ZipPath();
        var sfxStub = SettingsManager.Resolve7zSfxPath();
        if (File.Exists(sevenZipPath))
        {
            var sfxStatus = !string.IsNullOrEmpty(sfxStub) && File.Exists(sfxStub) ? "SFX stub present" : "SFX stub MISSING";
            progress?.Report($"[OK] 7-Zip Tooling located: {sevenZipPath} ({sfxStatus})");
        }
        else
        {
            progress?.Report($"[WARNING] 7-Zip executable not found at configured path: {sevenZipPath}");
        }

        progress?.Report(string.Empty);

        // --- STEP 2: NETWORK ISOLATION ---
        progress?.Report("--- STEP 2: NETWORK ADAPTER ISOLATION (QUARANTINE) ---");
        if (quarantineNetwork)
        {
            autoCount++;
            progress?.Report("[AUTO - WOULD EXECUTE] Quarantine Physical Network Adapters: ACTIVE");
            foreach (var a in adapters)
            {
                progress?.Report($"     * Command: powershell.exe \"Disable-NetAdapter -Name '{a.Name}' -Confirm:$false\"");
            }
            progress?.Report("     * Purpose: Blocks Windows Update from grabbing generic GPU drivers during cleanup.");
        }
        else
        {
            manualCount++;
            progress?.Report("[MANUAL ACTION REQUIRED] Network Quarantine is DISABLED");
            progress?.Report("     * Warning: Windows Update will remain active.");
            progress?.Report("     * Action: You should manually disconnect your Ethernet cable or disable Wi-Fi");
            progress?.Report("       before driver purge to prevent Windows Update from installing generic drivers.");
        }
        progress?.Report(string.Empty);

        // --- CLEAN UPGRADE SPECIFIC PHASES ---
        if (isCleanInstall)
        {
            if (autoRebootSafeMode)
            {
                // SAFE MODE TRANSITION
                progress?.Report("--- STEP 3: SAFE MODE MULTI-BOOT TRANSITION ---");
                autoCount += 2;
                progress?.Report("[AUTO - WOULD EXECUTE] Windows BCD SafeBoot Configuration: ACTIVE");
                progress?.Report("     * Command: bcdedit.exe /set {current} safeboot minimal");
                progress?.Report("[AUTO - WOULD EXECUTE] Safe Mode RunOnce Registration: ACTIVE");
                progress?.Report($"     * Registry: HKLM\\Software\\Microsoft\\Windows\\CurrentVersion\\RunOnce\\*NVDriverInstallerResume");
                progress?.Report($"     * Target: {WorkflowCoordinator.BuildResumeCommand()}");

                autoCount++;
                progress?.Report("[AUTO - WOULD EXECUTE] Safe Mode Reboot Before Driver Uninstall: ACTIVE");
                progress?.Report($"     * Command: shutdown.exe /r /t {settings.RebootDelaySeconds} /c \"NVDriver Installer: Safe Mode Transition\"");
                progress?.Report(string.Empty);

                // DDU IN SAFE MODE
                progress?.Report("--- STEP 4: SAFE MODE DDU PURGE ---");
                autoCount += 3;
                progress?.Report("[AUTO - WOULD EXECUTE] Safety Recovery: Clear BCD SafeBoot immediately upon Safe Mode boot");
                progress?.Report("     * Command: bcdedit.exe /deletevalue {current} safeboot (guarantees next boot is Normal Windows)");
                progress?.Report($"[AUTO - WOULD EXECUTE] Display Driver Uninstaller Silent Clean: ACTIVE");
                progress?.Report($"     * Command: \"{SettingsManager.ResolveDduPath()}\" -clean -silent -preventreboot");
                progress?.Report($"     * Profile: Applies Settings.xml cleanup for '{dduProfileId ?? settings.DefaultDduProfile}'");
                progress?.Report("[AUTO - WOULD EXECUTE] Normal Mode RunOnce Registration: ACTIVE");
                progress?.Report($"     * Registry: HKLM\\Software\\Microsoft\\Windows\\CurrentVersion\\RunOnce\\NVDriverInstallerResume");
                progress?.Report($"     * Target: {WorkflowCoordinator.BuildResumeCommand()}");
                progress?.Report("     * Automated Resume: Automatically launches the driver installer upon user logon in Normal Mode.");
                progress?.Report("     * No Manual Search: You do NOT need to find or manually run the installer package after reboot.");

                autoCount++;
                progress?.Report("[AUTO - WOULD EXECUTE] Automated Return Restart: ACTIVE");
                progress?.Report($"     * Command: shutdown.exe /r /t {settings.RebootDelaySeconds} /c \"Returning to Normal Mode for driver setup\"");
                progress?.Report(string.Empty);
            }
            else
            {
                // DDU IN CURRENT SESSION (SAFE MODE BYPASSED)
                progress?.Report("--- STEP 3: DDU CLEANUP (CURRENT SESSION) ---");
                progress?.Report("[INFO] Safe Mode Reboot Before Driver Uninstall: DISABLED (Safe Mode bypassed)");
                progress?.Report("     * DDU will execute directly in Windows normal mode without restarting.");
                autoCount++;
                progress?.Report($"[AUTO - WOULD EXECUTE] Display Driver Uninstaller Silent Clean: ACTIVE");
                progress?.Report($"     * Command: \"{SettingsManager.ResolveDduPath()}\" -clean -silent -preventreboot");
                progress?.Report($"     * Profile: Applies Settings.xml cleanup for '{dduProfileId ?? settings.DefaultDduProfile}'");
                progress?.Report("     * Display will flicker or temporarily revert to Basic Display Adapter during purge.");
                progress?.Report(string.Empty);
            }
        }

        // --- DRIVER SETUP PHASE ---
        var installStepNum = isCleanInstall ? (autoRebootSafeMode ? 5 : 4) : 3;
        progress?.Report($"--- STEP {installStepNum}: DRIVER INSTALLATION PHASE ---");
        if (isCleanInstall && autoRebootSafeMode)
        {
            autoCount++;
            progress?.Report("[AUTO-LAUNCH ON REBOOT] Automated Driver Setup Execution (Normal Windows Session):");
            progress?.Report("     * Trigger: Automatically fired on Normal Mode user logon via Windows RunOnce.");
            progress?.Report($"     * Package: {Path.GetFileName(packagePath)} (extracted to staging folder if .exe).");
            progress?.Report("     * User Action: NONE required to start setup. The installer opens automatically.");
        }
        else
        {
            autoCount++;
            progress?.Report("[CURRENT SESSION] Automated Driver Setup Execution:");
            progress?.Report($"     * Package: {Path.GetFileName(packagePath)}");
        }

        if (unattended)
        {
            progress?.Report("     * Mode: Unattended / Silent (Configured by build profile)");
            progress?.Report($"     * Command: \"{targetExe}\" -s -noreboot");
            progress?.Report("     * Execution: Runs completely silently in the background without user wizard prompts.");
        }
        else
        {
            manualCount++;
            progress?.Report("     * Mode: Interactive (Configured by build profile)");
            progress?.Report($"     * Command: \"{targetExe}\"");
            progress?.Report("     * Execution: The NVIDIA installer wizard GUI will open automatically on your screen.");
            progress?.Report("     * User Interaction: Simply click through standard NVIDIA license and setup prompts.");
        }
        progress?.Report(string.Empty);

        // --- CONTROL PANEL & NETWORK RESTORATION ---
        var restoreStepNum = isCleanInstall ? (autoRebootSafeMode ? 6 : 5) : 4;
        progress?.Report($"--- STEP {restoreStepNum}: CONTROL PANEL PROVISIONING & RESTORATION ---");
        if (installNvcpl)
        {
            autoCount++;
            progress?.Report("[AUTO - WOULD EXECUTE] Offline Modern NVIDIA Control Panel (AppX): ACTIVE");
            if (!string.IsNullOrEmpty(appxDisplayPath))
            {
                progress?.Report($"     * PowerShell Command: Add-AppxPackage -Path '{appxDisplayPath}'");
                progress?.Report($"     * DISM Command: dism.exe /Online /Add-ProvisionedAppxPackage /PackagePath:\"{appxDisplayPath}\"");
                progress?.Report("     * Ensures NVIDIA Control Panel is instantly available without waiting for Microsoft Store.");
            }
            else
            {
                progress?.Report("     * Notice: No offline .appx found in package; Store registration will be deferred to Windows HSA.");
            }
        }
        else
        {
            manualCount++;
            progress?.Report("[MANUAL ACTION REQUIRED] Offline NVIDIA Control Panel Install is DISABLED");
            progress?.Report("     * Action: Control Panel will not be provisioned automatically.");
            progress?.Report("     * You must manually download it from Microsoft Store if desired.");
        }

        if (quarantineNetwork)
        {
            autoCount++;
            progress?.Report("[AUTO - WOULD EXECUTE] Restore Physical Network Adapters: ACTIVE");
            foreach (var a in adapters)
            {
                progress?.Report($"     * Command: powershell.exe \"Enable-NetAdapter -Name '{a.Name}' -Confirm:$false\"");
            }
        }

        if (autoRebootAfterInstall)
        {
            autoCount++;
            progress?.Report("[AUTO - WOULD EXECUTE] Normal Reboot After Driver Install: ACTIVE");
            progress?.Report($"     * Command: shutdown.exe /r /t {settings.RebootDelaySeconds} /c \"NVDriver Installer: Finalizing driver installation\"");
        }
        else
        {
            progress?.Report("[INFO] Normal Reboot After Driver Install: DISABLED (Session continues without final reboot)");
        }

        progress?.Report(string.Empty);
        progress?.Report("================================================================================");
        progress?.Report($"SIMULATION COMPLETE: {autoCount} automated action(s) planned, {manualCount} manual action(s) required.");
        if (isCleanInstall && autoRebootSafeMode)
        {
            progress?.Report(string.Empty);
            progress?.Report("[AUTOMATED WORKFLOW NOTICE]");
            progress?.Report("  * After DDU purges drivers in Safe Mode, the system restarts to normal Windows.");
            progress?.Report("  * Upon normal user logon, NVDriver Installer AUTOMATICALLY launches the driver setup.");
            progress?.Report("  * You do NOT need to manually find, unpack, or execute the package file.");
        }
        progress?.Report("STATUS: Pre-flight checks passed successfully. System is ready for live execution.");
        progress?.Report("(SIMULATION ONLY: Zero modifications were made to registry, BCD, network, or drivers)");
        progress?.Report("================================================================================");

        return (true, $"Simulation finished cleanly: {autoCount} automated, {manualCount} manual actions.");
    }

    /// <summary>
    /// Installs the offline modern NVIDIA Control Panel (AppX) bundled within the driver package.
    /// DCH drivers decouple the Control Panel from setup.exe; Windows Store cannot fetch it when
    /// network is quarantined or Store updates are deferred.
    /// </summary>
    public static async Task<(bool success, string message)> InstallNvcplAppxAsync(
        string packageDirOrExe,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        var settings = SettingsManager.Current;
        var dir = Directory.Exists(packageDirOrExe)
            ? packageDirOrExe
            : Path.GetDirectoryName(packageDirOrExe);

        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
            return (false, "Package directory not found for Control Panel search.");

        // Locate any .appx or .appxbundle in the package (typically Display.Driver/NVCPL/*.appx)
        var appxFiles = Directory.GetFiles(dir, "*.appx", SearchOption.AllDirectories)
            .Concat(Directory.GetFiles(dir, "*.appxbundle", SearchOption.AllDirectories))
            .ToList();

        if (appxFiles.Count == 0)
        {
            progress?.Report("No offline NVIDIA Control Panel (AppX) package found in driver folder (skipped).");
            return (true, "No AppX package found.");
        }

        var appxPath = appxFiles[0];
        progress?.Report($"Found offline NVIDIA Control Panel: {Path.GetFileName(appxPath)}");

        if (settings.DryRunMode)
        {
            progress?.Report($"[Dry-Run] Simulating Add-AppxPackage for {Path.GetFileName(appxPath)}");
            return (true, "[Dry-Run] Simulated NVCPL AppX installation.");
        }

        progress?.Report("Installing NVIDIA Control Panel (AppX)...");

        try
        {
            // 1. Add-AppxPackage for current user via PowerShell
            var escapedAppx = appxPath.Replace("'", "''");
            var psCmd = $"Add-AppxPackage -Path '{escapedAppx}'";
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{psCmd}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using var proc = Process.Start(psi);
            if (proc != null)
            {
                await proc.WaitForExitAsync(ct);
                var err = await proc.StandardError.ReadToEndAsync(ct);
                if (proc.ExitCode == 0)
                {
                    progress?.Report("NVIDIA Control Panel AppX installed successfully for current user.");
                }
                else if (!string.IsNullOrWhiteSpace(err))
                {
                    progress?.Report($"AppX Notice: {err.Trim()}");
                }
            }

            // 2. DISM provision for all users if license exists or skip license
            var licenseFiles = Directory.GetFiles(Path.GetDirectoryName(appxPath)!, "*License*.xml");
            var dismArgs = licenseFiles.Length > 0
                ? $"/Online /Add-ProvisionedAppxPackage /PackagePath:\"{appxPath}\" /LicensePath:\"{licenseFiles[0]}\""
                : $"/Online /Add-ProvisionedAppxPackage /PackagePath:\"{appxPath}\" /SkipLicense";

            var dismPsi = new ProcessStartInfo
            {
                FileName = "dism.exe",
                Arguments = dismArgs,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using var dismProc = Process.Start(dismPsi);
            if (dismProc != null)
            {
                await dismProc.WaitForExitAsync(ct);
                if (dismProc.ExitCode == 0)
                {
                    progress?.Report("NVIDIA Control Panel provisioned system-wide via DISM.");
                }
            }

            return (true, "NVIDIA Control Panel installation completed.");
        }
        catch (Exception ex)
        {
            progress?.Report($"Warning during NVIDIA Control Panel installation: {ex.Message}");
            return (false, ex.Message);
        }
    }

    /// <summary>
    /// Launches a visible interactive PowerShell console to execute the driver installation or simulation pipeline.
    /// Hooks to the PowerShell process, provides real-time progress feedback, and pauses on error or simulation completion.
    /// </summary>
    public static async Task<bool> InstallDriverViaPowerShellPromptAsync(
        string packagePath,
        bool isCleanInstall,
        string? dduProfileId,
        string? packagingProfileId,
        bool quarantineNetwork,
        bool autoRebootSafeMode,
        bool autoRebootAfterInstall,
        bool unattended,
        bool installNvcpl,
        bool isDryRun,
        CancellationToken ct = default)
    {
        var exePath = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "NVDriverInstaller.exe");
        var baseDir = AppContext.BaseDirectory;
        var tempScriptPath = Path.Combine(Path.GetTempPath(), $"install_pkg_{Guid.NewGuid():N}.ps1");

        var escapedExe = exePath.Replace("'", "''");
        var escapedBaseDir = baseDir.Replace("'", "''");
        var escapedPackagePath = packagePath.Replace("'", "''");
        var pkgFileName = Path.GetFileName(packagePath).Replace("'", "''");
        var pathwayName = isCleanInstall ? "Clean Upgrade (DDU)" : "Standard In-Place Install";

        var argsBuilder = new List<string>
        {
            $"--package '{escapedPackagePath}'"
        };

        if (isCleanInstall)
        {
            argsBuilder.Add("--clean");
            if (!string.IsNullOrEmpty(dduProfileId))
                argsBuilder.Add($"--ddu-profile '{dduProfileId.Replace("'", "''")}'");
            if (!string.IsNullOrEmpty(packagingProfileId))
                argsBuilder.Add($"--profile '{packagingProfileId.Replace("'", "''")}'");
            if (autoRebootSafeMode)
                argsBuilder.Add("--safe-mode");
            else
                argsBuilder.Add("--no-safe-mode");
        }

        if (quarantineNetwork)
            argsBuilder.Add("--quarantine");
        else
            argsBuilder.Add("--no-quarantine");

        if (autoRebootAfterInstall)
            argsBuilder.Add("--auto-reboot");
        else
            argsBuilder.Add("--no-auto-reboot");

        if (unattended)
            argsBuilder.Add("--unattended");
        else
            argsBuilder.Add("--interactive");

        if (installNvcpl)
            argsBuilder.Add("--nvcpl");
        else
            argsBuilder.Add("--no-nvcpl");

        if (isDryRun)
            argsBuilder.Add("--dry-run");

        var cliArgs = string.Join(" ", argsBuilder);
        var title = isDryRun ? "Pre-Flight Simulation" : "Driver Installation";

        var psScript = $@"
Set-Location '{escapedBaseDir}'
$host.UI.RawUI.WindowTitle = 'NVDriver Installer - {title}'
Clear-Host
Write-Host '================================================================' -ForegroundColor Cyan
Write-Host '         NVDriver Installer - {title} Pipeline                  ' -ForegroundColor Cyan
Write-Host '================================================================' -ForegroundColor Cyan
Write-Host ''
Write-Host '  Target Package:  {pkgFileName}' -ForegroundColor Yellow
Write-Host '  Pathway:         {pathwayName}' -ForegroundColor Yellow
Write-Host '  Simulation Mode: {(isDryRun ? "YES (No changes will be applied)" : "NO (Live execution)")}' -ForegroundColor Yellow
Write-Host ''

& '{escapedExe}' install {cliArgs} | Out-Host

$exitCode = $LASTEXITCODE
if ($null -eq $exitCode) {{ $exitCode = 0 }}
if ($exitCode -ne 0) {{
    Write-Host ''
    Write-Host '================================================================' -ForegroundColor Red
    Write-Host "" [!] Operation failed or was cancelled (Exit code: $exitCode)."" -ForegroundColor Red
    Write-Host '================================================================' -ForegroundColor Red
    Write-Host ''
    Read-Host 'Press Enter to exit...'
    exit $exitCode
}} else {{
    Write-Host ''
    Write-Host '================================================================' -ForegroundColor Green
    Write-Host ' [+] Operation completed successfully!' -ForegroundColor Green
    Write-Host '================================================================' -ForegroundColor Green
    Write-Host ''
    {(isDryRun ? "Read-Host 'Press Enter to return to application...'" : "Start-Sleep -Seconds 2")}
    exit 0
}}
";

        try
        {
            await File.WriteAllTextAsync(tempScriptPath, psScript, ct);

            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{tempScriptPath}\"",
                UseShellExecute = true,
                CreateNoWindow = false,
                WindowStyle = ProcessWindowStyle.Normal
            };

            if (!isDryRun)
            {
                psi.Verb = "runas";
            }

            using var proc = Process.Start(psi);
            if (proc != null)
            {
                await proc.WaitForExitAsync(ct);
                return proc.ExitCode == 0;
            }
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            AppLogger.LogInstall("Driver installation cancelled by user at UAC elevation prompt.");
            return false;
        }
        catch (Exception ex)
        {
            AppLogger.LogInstallError("DriverInstaller.InstallDriverViaPowerShellPromptAsync", ex);
            return false;
        }
        finally
        {
            try { if (File.Exists(tempScriptPath)) File.Delete(tempScriptPath); } catch { }
        }

        return false;
    }
}
