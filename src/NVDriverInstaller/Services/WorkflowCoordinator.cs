using System.Diagnostics;
using System.IO;
using System.Text.Json;
using NVDriverInstaller.Core.Config;
using NVDriverInstaller.Core.Models;

namespace NVDriverInstaller.Services;

public static class WorkflowCoordinator
{
    private static readonly string StateFilePath = Path.Combine(AppContext.BaseDirectory, "workflow_state.json");

    public static WorkflowState? LoadState()
    {
        try
        {
            if (File.Exists(StateFilePath))
            {
                var json = File.ReadAllText(StateFilePath);
                return JsonSerializer.Deserialize<WorkflowState>(json);
            }
        }
        catch
        {
            // Ignore
        }
        return null;
    }

    public static void SaveState(WorkflowState state)
    {
        try
        {
            var json = JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(StateFilePath, json);
        }
        catch
        {
            // Ignore
        }
    }

    public static void ClearState()
    {
        try
        {
            if (File.Exists(StateFilePath))
                File.Delete(StateFilePath);
        }
        catch
        {
            // Ignore
        }
    }

    public static string BuildResumeCommand()
    {
        var appDir = AppContext.BaseDirectory;
        EnsureResumeScripts(appDir);
        var scriptsCmd = Path.Combine(appDir, "scripts", "resume.cmd");
        if (File.Exists(scriptsCmd))
        {
            return $"\"{scriptsCmd}\"";
        }
        var rootCmd = Path.Combine(appDir, "resume.cmd");
        return $"\"{rootCmd}\"";
    }

    public static void EnsureResumeScripts(string appDir)
    {
        try
        {
            var scriptsDir = Path.Combine(appDir, "scripts");
            Directory.CreateDirectory(scriptsDir);

            var cmdPath = Path.Combine(scriptsDir, "resume.cmd");
            var psPath = Path.Combine(scriptsDir, "resume-workflow.ps1");

            if (!File.Exists(cmdPath))
            {
                var cmdContent = "@echo off\r\nsetlocal\r\npushd \"%~dp0\"\r\nset \"PS_EXE=%SystemRoot%\\System32\\WindowsPowerShell\\v1.0\\powershell.exe\"\r\nstart \"NVDriver Installer - Pipeline Resume\" \"%PS_EXE%\" -NoProfile -ExecutionPolicy Bypass -WindowStyle Normal -File \"%~dp0resume-workflow.ps1\"\r\npopd\r\nendlocal\r\nexit /b 0\r\n";
                File.WriteAllText(cmdPath, cmdContent);
            }

            if (!File.Exists(psPath))
            {
                var candidatePaths = new[]
                {
                    Path.Combine(AppContext.BaseDirectory, "..", "..", "scripts", "resume-workflow.ps1"),
                    Path.Combine(AppContext.BaseDirectory, "resume-workflow.ps1"),
                    Path.Combine(AppContext.BaseDirectory, "..", "..", "resume-workflow.ps1")
                };

                foreach (var src in candidatePaths)
                {
                    if (File.Exists(src))
                    {
                        File.Copy(src, psPath, true);
                        break;
                    }
                }
            }

            // Clean up obsolete root-level resume scripts if they exist
            var oldRootCmd = Path.Combine(appDir, "resume.cmd");
            if (File.Exists(oldRootCmd))
            {
                try { File.Delete(oldRootCmd); } catch { }
            }
            var oldRootPs = Path.Combine(appDir, "resume-workflow.ps1");
            if (File.Exists(oldRootPs))
            {
                try { File.Delete(oldRootPs); } catch { }
            }
        }
        catch
        {
            // Ignore script write errors if protected
        }
    }

    public static async Task<(bool success, string message)> StartCleanUpgradeWorkflowAsync(
        string packagePath,
        string packagingProfileId,
        string dduProfileId)
    {
        if (!SystemInspector.IsElevated())
            return (false, "Administrator elevation required to configure Safe Mode and network adapters.");

        if (!DduManager.IsInstalled())
            return (false, "Cannot arm Safe Mode Clean Upgrade: Display Driver Uninstaller (DDU) is not installed. Please download DDU in Tools & Utilities.");

        if (!File.Exists(packagePath) && !Directory.Exists(packagePath))
            return (false, $"Driver package does not exist: {packagePath}");

        var dduProfile = ProfileManager.GetDduProfile(dduProfileId);
        var pkgProfile = ProfileManager.GetPackagingProfile(packagingProfileId);
        var settings = SettingsManager.Current;

        // 1. Sync DDU settings
        DduManager.ApplyProfileSettings(dduProfile);

        // 2. Network quarantine
        if (settings.QuarantineNetwork)
        {
            NetworkQuarantineService.DisableAllPhysical();
        }

        // 3. Save state
        var state = new WorkflowState
        {
            Phase = WorkflowPhase.Prepared,
            PackagePath = packagePath,
            NetworkDisabled = settings.QuarantineNetwork,
            DduProfile = dduProfile.Id,
            PackagingProfile = pkgProfile.Id,
            AutoRebootToSafeMode = settings.AutoRebootToSafeMode,
            AutoRebootAfterInstall = settings.AutoRebootAfterInstall,
            UnattendedInstall = pkgProfile.Unattended,
            InstallNvcplAppx = pkgProfile.HasControlPanel
        };
        SaveState(state);

        // 4. Register Safe Mode RunOnce
        var resumeCmd = BuildResumeCommand();
        var regOk = BcdBootManager.RegisterRunOnce(settings.SafeModeRunOnceName, resumeCmd, safeMode: true);
        if (!regOk)
        {
            CancelWorkflow();
            return (false, "Failed to register Safe Mode RunOnce entry in registry.");
        }

        // 5. Enable BCD SafeBoot Minimal
        var bcdOk = BcdBootManager.EnableSafeMode();
        if (!bcdOk)
        {
            CancelWorkflow();
            return (false, "Failed to configure Windows BCD for Safe Mode.");
        }

        // 6. Schedule restart into Safe Mode if enabled
        if (settings.AutoRebootToSafeMode)
        {
            BcdBootManager.ScheduleReboot(settings.RebootDelaySeconds, "NVDriver Installer: Safe Mode DDU Transition");
        }

        return (true, settings.AutoRebootToSafeMode
            ? $"Safe Mode transition armed. System restarting in {settings.RebootDelaySeconds}s."
            : "Safe Mode transition armed. SafeBoot minimal configured; please restart manually to enter Safe Mode.");
    }

    public static async Task<(bool success, string message)> ResumeWorkflowAsync(
        IProgress<string>? statusCallback = null,
        CancellationToken ct = default)
    {
        var state = LoadState();
        if (state == null)
            return (false, "No active driver upgrade workflow found.");

        var settings = SettingsManager.Current;
        var inSafeMode = SystemInspector.IsSafeMode();

        if (inSafeMode)
        {
            statusCallback?.Report("Safe Mode detected. Resetting BCD SafeBoot to guarantee normal reboot...");
            // CRITICAL SAFETY: Always disable SafeBoot first upon entering Safe Mode
            BcdBootManager.DisableSafeMode();

            // Run DDU silently
            statusCallback?.Report("Executing silent Display Driver Uninstaller clean...");
            statusCallback?.Report("Please wait: DDU is purging GPU drivers, cache, and registry keys (~1-2 min)...");
            var dduProf = ProfileManager.GetDduProfile(state.DduProfile);
            var (dduOk, dduMsg) = await DduManager.ExecuteSilentCleanAsync(dduProf, ct);

            if (!dduOk)
            {
                statusCallback?.Report($"[ERROR] DDU cleanup failed: {dduMsg}");
                AppLogger.LogInstall($"DDU silent clean failed: {dduMsg}", "ERROR");
                state.Phase = WorkflowPhase.Failed;
                state.Error = dduMsg;
                SaveState(state);

                if (state.NetworkDisabled)
                {
                    statusCallback?.Report("Restoring physical network adapters...");
                    NetworkQuarantineService.RestoreAllAdapters();
                }

                return (false, $"DDU cleanup failed: {dduMsg}");
            }

            statusCallback?.Report("DDU cleanup finished successfully!");

            // Register Normal Mode RunOnce
            BcdBootManager.UnregisterRunOnce(settings.SafeModeRunOnceName);
            var resumeCmd = BuildResumeCommand();
            BcdBootManager.RegisterRunOnce(settings.NormalModeRunOnceName, resumeCmd, safeMode: false);

            // Update state
            state.Phase = WorkflowPhase.SafeModeDdu;
            state.Error = null;
            SaveState(state);

            // Schedule return reboot
            if (state.AutoReboot)
            {
                statusCallback?.Report($"DDU cleanup finished. Returning to Normal Mode in {settings.RebootDelaySeconds}s...");
                BcdBootManager.ScheduleReboot(settings.RebootDelaySeconds, "NVDriver Installer: Returning to Normal Mode for driver install");
            }
            else
            {
                statusCallback?.Report("DDU cleanup finished. SafeBoot cleared. Please reboot back to normal Windows manually.");
            }

            return (true, "DDU cleanup completed. System scheduled to return to Normal Mode.");
        }

        // Resumed in Normal Mode
        statusCallback?.Report("Resumed in Normal Mode. Finalizing driver installation...");

        // Clear all RunOnce entries and safety-clear BCD
        BcdBootManager.UnregisterRunOnce(settings.SafeModeRunOnceName);
        BcdBootManager.UnregisterRunOnce(settings.NormalModeRunOnceName);
        BcdBootManager.DisableSafeMode();

        // Execute driver installation (handles extracting .exe or directory package, and offline NVCPL AppX)
        var (installOk, installMsg) = await DriverInstaller.InstallDriverAsync(
            state.PackagePath,
            state.UnattendedInstall,
            state.InstallNvcplAppx,
            statusCallback,
            ct);

        if (!installOk)
        {
            statusCallback?.Report($"[ERROR] Driver installation failed: {installMsg}");
            AppLogger.LogInstall($"Workflow resume installer failed: {installMsg}", "ERROR");

            if (state.NetworkDisabled)
            {
                statusCallback?.Report("Restoring physical network adapters...");
                NetworkQuarantineService.RestoreAllAdapters();
            }

            state.Phase = WorkflowPhase.Failed;
            state.Error = installMsg;
            SaveState(state);

            return (false, $"Driver installation failed: {installMsg}");
        }

        // Restore network adapters
        if (state.NetworkDisabled)
        {
            statusCallback?.Report("Restoring physical network adapters...");
            NetworkQuarantineService.RestoreAllAdapters();
        }

        ClearState();
        statusCallback?.Report("NVIDIA clean driver upgrade finished successfully!");

        // 7. Normal reboot after driver install if enabled
        if (state.AutoRebootAfterInstall)
        {
            statusCallback?.Report($"Scheduling normal system reboot in {settings.RebootDelaySeconds}s to finalize installation...");
            BcdBootManager.ScheduleReboot(settings.RebootDelaySeconds, "NVDriver Installer: Finalizing driver installation");
        }
        else
        {
            statusCallback?.Report("Normal reboot after install is disabled. System ready.");
        }

        return (true, "Driver upgrade pipeline completed.");
    }

    public static (bool success, string message) CancelWorkflow()
    {
        var settings = SettingsManager.Current;

        BcdBootManager.AbortReboot();
        BcdBootManager.DisableSafeMode();
        BcdBootManager.UnregisterRunOnce(settings.SafeModeRunOnceName);
        BcdBootManager.UnregisterRunOnce(settings.NormalModeRunOnceName);

        var restored = NetworkQuarantineService.RestoreAllAdapters();
        ClearState();

        return (true, $"Workflow aborted. SafeBoot cleared and {restored.Count} adapter(s) restored.");
    }
}
