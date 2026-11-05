using System.IO;
using NVDriverInstaller.Core.Config;
using NVDriverInstaller.Core.Models;
using NVDriverInstaller.Services;

namespace NVDriverInstaller;

public static class CliDispatcher
{
    public static async Task RunAsync(string[] args)
    {
        if (args.Length == 0 || args[0].Equals("--help", StringComparison.OrdinalIgnoreCase) || args[0].Equals("-h", StringComparison.OrdinalIgnoreCase))
        {
            PrintHelp();
            return;
        }

        var command = args[0].ToLowerInvariant();
        switch (command)
        {
            case "inspect":
                RunInspect();
                break;

            case "network":
                RunNetwork(args.Skip(1).ToArray());
                break;

            case "ddu":
                await RunDduAsync(args.Skip(1).ToArray());
                break;

            case "build":
                await RunBuildAsync(args.Skip(1).ToArray());
                break;

            case "install":
                await RunInstallAsync(args.Skip(1).ToArray());
                break;

            default:
                Console.WriteLine($"Unknown command: {command}");
                PrintHelp();
                Environment.ExitCode = 1;
                break;
        }
    }

    private static void PrintHelp()
    {
        Console.WriteLine(@"
NVDriver Installer - Command Line Interface
Usage: NVDriverInstaller.exe <command> [options]

Commands:
  inspect                     Display host hardware, active GPU, and driver version
  network [status|disable|restore]
                              Inspect or toggle network adapter isolation
  ddu [--profile <id>]        Execute silent Display Driver Uninstaller wipe
  build [--profile <id>] [--version <ver>] [--driver-type <type>] [--url <url>] [--local-file <path>]
                              Download, prune, and package driver without installing
  install [--package <path>] [--clean] [--ddu-profile <id>] [--safe-mode] [--auto-reboot] [--dry-run]
                              Initiate clean or standard driver installation pipeline

Examples:
  NVDriverInstaller.exe inspect
  NVDriverInstaller.exe install --package ""packages\clean\Clean_NVIDIA_560.94_Setup.exe"" --clean
  NVDriverInstaller.exe build --profile standard --version 560.94

");
    }

    private static void RunInspect()
    {
        Console.WriteLine("[*] Inspecting system hardware...");
        var host = SystemInspector.InspectHost();

        Console.WriteLine($"Hostname:      {host.Hostname}");
        Console.WriteLine($"Manufacturer:  {host.Manufacturer}");
        Console.WriteLine($"Model:         {host.Model}");
        Console.WriteLine($"Form Factor:   {host.FormFactor}");
        Console.WriteLine($"Elevated:      {host.IsElevated}");
        Console.WriteLine($"Safe Mode:     {host.IsSafeMode}");
        Console.WriteLine("\nDetected Display Controllers:");

        foreach (var gpu in host.Gpus)
        {
            var badge = gpu.IsNvidia ? "[NVIDIA ACTIVE]" : "[OTHER]";
            Console.WriteLine($"  * {gpu.Name} {badge}");
            Console.WriteLine($"    Driver Version:  {gpu.FormattedNvidiaVersion} (raw: {gpu.RawDriverVersion})");
            Console.WriteLine($"    PNP Device ID:   {gpu.PnpDeviceId}");
        }
    }

    private static void RunNetwork(string[] args)
    {
        var sub = args.Length > 0 ? args[0].ToLowerInvariant() : "status";

        switch (sub)
        {
            case "disable":
                Console.WriteLine("[*] Disabling physical network adapters...");
                var disabled = NetworkQuarantineService.DisableAllPhysical();
                Console.WriteLine($"Disabled {disabled.Count} adapter(s): {string.Join(", ", disabled)}");
                break;

            case "restore":
                Console.WriteLine("[*] Restoring physical network adapters...");
                var restored = NetworkQuarantineService.RestoreAllAdapters();
                Console.WriteLine($"Restored {restored.Count} adapter(s): {string.Join(", ", restored)}");
                break;

            case "status":
            default:
                Console.WriteLine("[*] Physical Network Adapters:");
                var adapters = NetworkQuarantineService.GetPhysicalAdapters();
                foreach (var a in adapters)
                {
                    Console.WriteLine($"  * {a.Name} [{a.AdminStatus}] - {a.InterfaceDescription}");
                }
                break;
        }
    }

    private static async Task RunDduAsync(string[] args)
    {
        var profileId = GetArg(args, "--profile") ?? "standard";
        var profile = ProfileManager.GetDduProfile(profileId);

        Console.WriteLine($"[*] Executing DDU silent clean using profile '{profile.Name}'...");
        var (ok, msg) = await DduManager.ExecuteSilentCleanAsync(profile);
        Console.WriteLine(ok ? $"[+] {msg}" : $"[-] Error: {msg}");
    }

    private static async Task RunBuildAsync(string[] args)
    {
        var profileId = GetArg(args, "--profile") ?? "standard";
        var version = GetArg(args, "--version");
        var driverTypeArg = GetArg(args, "--driver-type");
        var urlArg = GetArg(args, "--url");
        var localFileArg = GetArg(args, "--local-file");

        var profile = ProfileManager.GetPackagingProfile(profileId);

        var host = SystemInspector.InspectHost();
        var detectedType = host.FormFactor.Equals("Notebook", StringComparison.OrdinalIgnoreCase) ? "notebook" : "desktop";
        var driverType = !string.IsNullOrEmpty(driverTypeArg)
            ? driverTypeArg
            : (!string.IsNullOrEmpty(profile.DriverType) && !profile.DriverType.Equals("desktop", StringComparison.OrdinalIgnoreCase)
                ? profile.DriverType
                : detectedType);

        DriverPackage? target = null;

        if (!string.IsNullOrEmpty(localFileArg) && File.Exists(localFileArg))
        {
            target = new DriverPackage
            {
                Version = version ?? "Custom",
                DriverType = driverType,
                LocalFilePath = localFileArg
            };
        }
        else if (!string.IsNullOrEmpty(urlArg))
        {
            target = new DriverPackage
            {
                Version = version ?? "Latest",
                DriverType = driverType,
                DownloadUrl = urlArg
            };
        }
        else
        {
            Console.WriteLine($"[*] Querying driver catalog for {driverType}...");
            var packages = await NvidiaCatalogClient.FetchAvailablePackagesAsync(driverType);

            target = !string.IsNullOrEmpty(version)
                ? packages.FirstOrDefault(p => p.Version.Equals(version, StringComparison.OrdinalIgnoreCase)) ?? new DriverPackage { Version = version, DriverType = driverType }
                : packages.FirstOrDefault();
        }

        if (target == null)
        {
            Console.WriteLine("[-] No driver packages available.");
            Environment.ExitCode = 1;
            return;
        }

        Console.WriteLine($"[*] Building clean package for NVIDIA v{target.Version} ({profile.Name})...");
        var progress = new Progress<string>(Console.WriteLine);
        var (ok, outPath, msg) = await PackageBuilder.BuildPackageAsync(profile, target, progress);
        if (ok)
        {
            Console.WriteLine($"[+] Success: {msg}\nOutput: {outPath}");
            Environment.ExitCode = 0;
        }
        else
        {
            Console.WriteLine($"[-] Error: {msg}");
            Environment.ExitCode = 1;
        }
    }

    private static async Task RunInstallAsync(string[] args)
    {
        var packagePath = GetArg(args, "--package");
        var isClean = HasFlag(args, "--clean");
        var isDryRun = HasFlag(args, "--dry-run") || HasFlag(args, "--simulate");
        var dduProfileId = GetArg(args, "--ddu-profile") ?? SettingsManager.Current.DefaultDduProfile ?? "standard";
        var packagingProfileId = GetArg(args, "--profile") ?? "standard";

        var quarantine = HasFlag(args, "--quarantine") || (!HasFlag(args, "--no-quarantine") && SettingsManager.Current.QuarantineNetwork);
        var safeMode = HasFlag(args, "--safe-mode") || (!HasFlag(args, "--no-safe-mode") && SettingsManager.Current.AutoRebootToSafeMode);
        var autoReboot = HasFlag(args, "--auto-reboot") || (!HasFlag(args, "--no-auto-reboot") && SettingsManager.Current.AutoRebootAfterInstall);
        var installNvcpl = !HasFlag(args, "--no-nvcpl");

        // Resolve package if not explicitly provided
        if (string.IsNullOrEmpty(packagePath))
        {
            var scanned = PackageBuilder.ScanLocalPackages();
            packagePath = scanned.FirstOrDefault()?.LocalFilePath;
        }

        if (string.IsNullOrEmpty(packagePath) || (!File.Exists(packagePath) && !Directory.Exists(packagePath)))
        {
            Console.WriteLine("[-] Driver package not found. Please specify --package <path> or build a package first.");
            Environment.ExitCode = 1;
            return;
        }

        // Only unattended if explicitly flagged, or if the package's build profile specifies unattended = true
        bool unattended;
        if (HasFlag(args, "--unattended"))
        {
            unattended = true;
        }
        else if (HasFlag(args, "--interactive"))
        {
            unattended = false;
        }
        else
        {
            var profName = packagingProfileId;
            if ((string.IsNullOrEmpty(profName) || profName.Equals("standard", StringComparison.OrdinalIgnoreCase)) && !string.IsNullOrEmpty(packagePath))
            {
                var scanned = PackageBuilder.ScanLocalPackages().FirstOrDefault(p => p.LocalFilePath?.Equals(packagePath, StringComparison.OrdinalIgnoreCase) == true);
                if (!string.IsNullOrEmpty(scanned?.ProfileName))
                    profName = scanned.ProfileName;
            }
            var prof = !string.IsNullOrEmpty(profName) ? ProfileManager.GetPackagingProfile(profName) : null;
            unattended = prof?.Unattended ?? false;
        }

        Console.WriteLine($"[*] Driver package: {Path.GetFileName(packagePath)}");
        Console.WriteLine($"[*] Installation mode: {(unattended ? "Unattended / Silent" : "Interactive (NVIDIA GUI wizard)")}");

        var progress = new Progress<string>(Console.WriteLine);

        if (isDryRun)
        {
            Console.WriteLine("[*] Starting Pre-Flight Simulation Plan...");
            var (simOk, _) = await DriverInstaller.RunSimulationAsync(
                packagePath: packagePath,
                isCleanInstall: isClean,
                dduProfileId: dduProfileId,
                quarantineNetwork: quarantine,
                autoRebootSafeMode: safeMode,
                autoRebootAfterInstall: autoReboot,
                unattended: unattended,
                installNvcpl: installNvcpl,
                progress: progress);

            Environment.ExitCode = simOk ? 0 : 1;
            return;
        }

        if (isClean)
        {
            if (safeMode)
            {
                Console.WriteLine("[*] Arming Safe Mode Clean Upgrade Workflow...");
                var (ok, msg) = await WorkflowCoordinator.StartCleanUpgradeWorkflowAsync(
                    packagePath,
                    packagingProfileId,
                    dduProfileId);

                Console.WriteLine(ok ? $"[+] {msg}" : $"[-] Error: {msg}");
                Environment.ExitCode = ok ? 0 : 1;
                return;
            }
            else
            {
                Console.WriteLine("[*] Starting Clean Upgrade in current session (Safe Mode bypassed)...");

                var quarantined = false;
                if (quarantine)
                {
                    Console.WriteLine("[*] Quarantining physical network adapters...");
                    var disabled = NetworkQuarantineService.DisableAllPhysical();
                    quarantined = disabled.Count > 0;
                    Console.WriteLine($"[+] Quarantined {disabled.Count} physical network adapter(s).");
                }

                try
                {
                    var dduProfile = ProfileManager.GetDduProfile(dduProfileId);
                    Console.WriteLine($"[*] Executing DDU silent clean (Profile: {dduProfile.DisplayName})...");
                    var (dduOk, dduMsg) = await DduManager.ExecuteSilentCleanAsync(dduProfile);
                    Console.WriteLine(dduOk ? $"[+] {dduMsg}" : $"[-] DDU Warning: {dduMsg}");

                    Console.WriteLine($"[*] Installing driver package: {Path.GetFileName(packagePath)}...");
                    var (instOk, instMsg) = await DriverInstaller.InstallDriverAsync(
                        packagePath,
                        unattended: unattended,
                        installNvcpl: installNvcpl,
                        progress: progress);

                    if (!instOk)
                    {
                        Console.WriteLine($"[-] Installation failed: {instMsg}");
                        Environment.ExitCode = 1;
                    }
                    else
                    {
                        Console.WriteLine($"[+] Installation complete: {instMsg}");
                        if (autoReboot)
                        {
                            Console.WriteLine("[*] Restarting system as requested...");
                            BcdBootManager.ScheduleReboot(10, "NVDriver Installer: Restarting after clean driver installation");
                        }
                        Environment.ExitCode = 0;
                    }
                }
                finally
                {
                    if (quarantined)
                    {
                        Console.WriteLine("[*] Restoring physical network adapters...");
                        var restored = NetworkQuarantineService.RestoreAllAdapters();
                        Console.WriteLine($"[+] Restored {restored.Count} network adapter(s).");
                    }
                }
                return;
            }
        }
        else
        {
            // Standard in-place install
            Console.WriteLine("[*] Starting Standard In-Place Driver Installation...");
            var quarantined = false;
            if (quarantine)
            {
                Console.WriteLine("[*] Quarantining physical network adapters...");
                var disabled = NetworkQuarantineService.DisableAllPhysical();
                quarantined = disabled.Count > 0;
                Console.WriteLine($"[+] Quarantined {disabled.Count} physical network adapter(s).");
            }

            try
            {
                Console.WriteLine($"[*] Installing driver package: {Path.GetFileName(packagePath)}...");
                var (instOk, instMsg) = await DriverInstaller.InstallDriverAsync(
                    packagePath,
                    unattended: unattended,
                    installNvcpl: installNvcpl,
                    progress: progress);

                if (!instOk)
                {
                    Console.WriteLine($"[-] Installation failed: {instMsg}");
                    Environment.ExitCode = 1;
                }
                else
                {
                    Console.WriteLine($"[+] Installation complete: {instMsg}");
                    if (autoReboot)
                    {
                        Console.WriteLine("[*] Restarting system as requested...");
                        BcdBootManager.ScheduleReboot(10, "NVDriver Installer: Restarting after driver installation");
                    }
                    Environment.ExitCode = 0;
                }
            }
            finally
            {
                if (quarantined)
                {
                    Console.WriteLine("[*] Restoring physical network adapters...");
                    var restored = NetworkQuarantineService.RestoreAllAdapters();
                    Console.WriteLine($"[+] Restored {restored.Count} network adapter(s).");
                }
            }
        }
    }

    private static bool HasFlag(string[] args, string flag)
    {
        return args.Any(a => a.Equals(flag, StringComparison.OrdinalIgnoreCase));
    }

    private static string? GetArg(string[] args, string flag)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i].Equals(flag, StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        }
        return null;
    }
}
