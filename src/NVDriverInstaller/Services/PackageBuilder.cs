using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using NVDriverInstaller.Core.Config;
using NVDriverInstaller.Core.Models;
using SharpCompress.Archives;
using SharpCompress.Archives.SevenZip;
using SharpCompress.Common;
using SharpCompress.Readers;

namespace NVDriverInstaller.Services;

public static class PackageBuilder
{
    private static readonly Regex PackageDirRegex = new(
        @"^Clean_NVIDIA_([0-9.]+)_([a-zA-Z0-9]+)_([a-zA-Z0-9_]+)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex PackagePatternRegex = new(
        @"(?:Clean_|NVCleanstall_)?NVIDIA_([0-9.]+)(?:_x64)?(?:_dch)?_(Desktop|Notebook)(?:_([a-zA-Z0-9_-]+?))?(?:_Setup)?(?:\.exe)?$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static List<DriverPackage> ScanLocalPackages(string? directory = null)
    {
        var packages = new List<DriverPackage>();
        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(directory))
        {
            ScanDirectory(directory, isCleanDir: true, packages, seenPaths);
        }
        else
        {
            // 1. Primary scan: packages/clean/
            var cleanDir = SettingsManager.ResolveCleanPackagesDir();
            ScanDirectory(cleanDir, isCleanDir: true, packages, seenPaths);

            // 2. Secondary fallback scan: legacy root packages/
            var rootDir = SettingsManager.ResolveDownloadsDir();
            ScanDirectory(rootDir, isCleanDir: false, packages, seenPaths);
        }

        return packages.OrderByDescending(p => NvidiaCatalogClient.ParseVersion(p.Version)).ToList();
    }

    private static void ScanDirectory(string dirPath, bool isCleanDir, List<DriverPackage> packages, HashSet<string> seenPaths)
    {
        if (!Directory.Exists(dirPath))
            return;

        // 1. Scan for built package directories
        foreach (var subDir in Directory.GetDirectories(dirPath))
        {
            var fullSubDir = Path.GetFullPath(subDir);
            if (seenPaths.Contains(fullSubDir)) continue;

            // Don't scan raw or clean as package directories when scanning root packages/
            var dirName = Path.GetFileName(fullSubDir);
            if (dirName.Equals("raw", StringComparison.OrdinalIgnoreCase) ||
                dirName.Equals("clean", StringComparison.OrdinalIgnoreCase))
                continue;

            if (TryParsePackage(fullSubDir, isCleanDir, out var pkg) && pkg != null)
            {
                seenPaths.Add(fullSubDir);
                packages.Add(pkg);
            }
        }

        // 2. Scan for built package .exe files
        foreach (var file in Directory.GetFiles(dirPath, "*.exe"))
        {
            var fullFile = Path.GetFullPath(file);
            if (seenPaths.Contains(fullFile)) continue;

            if (TryParsePackage(fullFile, isCleanDir, out var pkg) && pkg != null)
            {
                seenPaths.Add(fullFile);
                packages.Add(pkg);
            }
        }
    }

    private static bool TryParsePackage(string path, bool isCleanDir, out DriverPackage? package)
    {
        package = null;
        var isDir = Directory.Exists(path);
        var isFile = File.Exists(path);

        if (!isDir && !isFile) return false;

        var name = Path.GetFileName(path);
        if (isFile)
        {
            if (!name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return false;
            if (name.Equals("Display Driver Uninstaller.exe", StringComparison.OrdinalIgnoreCase)) return false;
            if (name.Equals("7z.exe", StringComparison.OrdinalIgnoreCase)) return false;
        }
        else // isDir
        {
            if (!File.Exists(Path.Combine(path, "setup.exe"))) return false;
        }

        // If not in clean folder, only recognize explicitly clean or NVCleanstall builds
        if (!isCleanDir)
        {
            if (!name.StartsWith("Clean_", StringComparison.OrdinalIgnoreCase) &&
                !name.StartsWith("NVCleanstall_", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        var patternMatch = PackagePatternRegex.Match(name);
        string version;
        string type = "desktop";
        string? profile = null;

        if (patternMatch.Success)
        {
            version = patternMatch.Groups[1].Value;
            type = patternMatch.Groups[2].Value.ToLowerInvariant();
            if (patternMatch.Groups.Count >= 4 && patternMatch.Groups[3].Success)
            {
                var prof = patternMatch.Groups[3].Value;
                if (!prof.Equals("Setup", StringComparison.OrdinalIgnoreCase))
                {
                    profile = prof;
                }
            }
        }
        else
        {
            var dirMatch = PackageDirRegex.Match(name);
            if (dirMatch.Success)
            {
                version = dirMatch.Groups[1].Value;
                type = dirMatch.Groups[2].Value.ToLowerInvariant();
                profile = dirMatch.Groups[3].Value;
            }
            else
            {
                var verMatch = Regex.Match(name, @"([0-9]{3}\.[0-9]{2})");
                if (!verMatch.Success) return false;
                version = verMatch.Groups[1].Value;
                if (name.Contains("notebook", StringComparison.OrdinalIgnoreCase))
                    type = "notebook";
            }
        }

        long size;
        if (isFile)
        {
            try { size = new FileInfo(path).Length; } catch { size = 0; }
        }
        else
        {
            size = GetDirectorySize(path);
        }

        package = new DriverPackage
        {
            Version = version,
            DriverType = type,
            Dch = true,
            IsBuilt = true,
            ProfileName = profile,
            LocalFilePath = path,
            FileSizeBytes = size
        };
        return true;
    }

    public static long GetDirectorySize(string directory)
    {
        try
        {
            var d = new DirectoryInfo(directory);
            return d.EnumerateFiles("*", SearchOption.AllDirectories).Sum(fi => fi.Length);
        }
        catch
        {
            return 0;
        }
    }

    private static void CopyDirectory(string sourceDir, string destinationDir)
    {
        var dir = new DirectoryInfo(sourceDir);
        if (!dir.Exists) throw new DirectoryNotFoundException($"Source directory not found: {dir.FullName}");

        Directory.CreateDirectory(destinationDir);

        foreach (var file in dir.GetFiles())
        {
            var targetFilePath = Path.Combine(destinationDir, file.Name);
            file.CopyTo(targetFilePath, overwrite: true);
        }

        foreach (var subDir in dir.GetDirectories())
        {
            var nextTargetSubDir = Path.Combine(destinationDir, subDir.Name);
            CopyDirectory(subDir.FullName, nextTargetSubDir);
        }
    }

    public static void PruneUnselectedComponents(string extractDir, PackagingProfile profile)
    {
        var dirInfo = new DirectoryInfo(extractDir);
        if (!dirInfo.Exists) return;

        var keepDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Display.Driver",
            "NVI2"
        };

        if (profile.HasControlPanel) keepDirs.Add("NvCpl");
        if (profile.HasPhysX) keepDirs.Add("PhysX");
        if (profile.HasAudio) keepDirs.Add("HDAudio");
        if (profile.HasPlatform)
        {
            keepDirs.Add("NVPCF");
            keepDirs.Add("PPC");
        }

        var purgeTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "NvTelemetry",
            "GFExperience",
            "GeForceExperience",
            "ShadowPlay",
            "ShieldWirelessController",
            "FrameViewSDK",
            "NView",
            "NVWMI",
            "Ansel",
            "VirtualAudio",
            "VirtualAudio.Driver",
            "PPC"
        };

        if (!profile.HasControlPanel) purgeTargets.Add("NvCpl");
        if (!profile.HasPlatform) purgeTargets.Add("NVPCF");
        if (!profile.HasPhysX) purgeTargets.Add("PhysX");
        if (!profile.HasAudio) purgeTargets.Add("HDAudio");

        foreach (var subDir in dirInfo.GetDirectories())
        {
            if (purgeTargets.Contains(subDir.Name) || !keepDirs.Contains(subDir.Name))
            {
                try { subDir.Delete(recursive: true); } catch { }
            }
        }

        // Strip telemetry plugins inside Display.Driver/Display.NvContainer/plugins
        // Note: Do NOT delete files directly from Display.Driver root (such as NvTelemetry64.dll)
        // because the driver INF and Driver Store require them for WHQL package staging.
        if (profile.DisableTelemetry)
        {
            var pluginDir = Path.Combine(extractDir, "Display.Driver", "Display.NvContainer", "plugins");
            if (Directory.Exists(pluginDir))
            {
                foreach (var file in Directory.GetFiles(pluginDir, "*telemetry*", SearchOption.AllDirectories))
                {
                    try { File.Delete(file); } catch { }
                }
            }
        }
    }

    public static void PatchManifests(string extractDir, PackagingProfile profile)
    {
        // 1. setup.cfg patching
        var setupCfgPath = Path.Combine(extractDir, "setup.cfg");
        if (File.Exists(setupCfgPath))
        {
            try
            {
                var doc = XDocument.Load(setupCfgPath);
                var root = doc.Root;

                if (root != null)
                {
                    if (profile.CleanInstall)
                    {
                        var optionsElem = root.Element("options");
                        if (optionsElem != null)
                        {
                            var cleanInstallElem = optionsElem.Elements("bool")
                                .FirstOrDefault(b => (string?)b.Attribute("name") == "clean");
                            if (cleanInstallElem != null)
                            {
                                cleanInstallElem.SetAttributeValue("value", "true");
                            }
                            else
                            {
                                optionsElem.Add(new XElement("bool",
                                    new XAttribute("name", "clean"),
                                    new XAttribute("value", "true")));
                            }
                        }
                    }

                    // Universal manifest sanitizer: remove any <file> whose target does not exist on disk
                    var manifestElem = root.Element("manifest");
                    if (manifestElem != null)
                    {
                        var filesToRemove = manifestElem.Elements("file")
                            .Where(f =>
                            {
                                var rawName = (string?)f.Attribute("name") ?? "";
                                if (string.IsNullOrWhiteSpace(rawName)) return true;

                                if (profile.DisableTelemetry && rawName.Contains("telemetry", StringComparison.OrdinalIgnoreCase))
                                    return true;

                                // Expand variables used in setup.cfg manifest
                                var expanded = rawName
                                    .Replace("${{InstallerDllDirectory}}", "NVI2")
                                    .Replace("${{ResourcesDirectory}}", "NVI2")
                                    .Replace("${PrivacyPolicyFile}", "EULA.txt")
                                    .Replace("${{PrivacyPolicyFile}}", "EULA.txt")
                                    .Replace("${{EulaHtmlFile}}", "EULA.txt")
                                    .Replace("${{FunctionalConsentFile}}", "EULA.txt");

                                if (expanded.Contains('*'))
                                {
                                    var dirPart = Path.GetDirectoryName(expanded);
                                    var searchPattern = Path.GetFileName(expanded);
                                    var resolvedDir = string.IsNullOrEmpty(dirPart) ? extractDir : Path.Combine(extractDir, dirPart);
                                    return !Directory.Exists(resolvedDir) || !Directory.EnumerateFiles(resolvedDir, searchPattern).Any();
                                }

                                var fullPath = Path.Combine(extractDir, expanded);
                                return !File.Exists(fullPath) && !Directory.Exists(fullPath);
                            })
                            .ToList();

                        foreach (var f in filesToRemove)
                            f.Remove();
                    }

                    // Ensure documentation string references point to an existing file
                    var docStringsToFix = root.Descendants("string")
                        .Where(s =>
                        {
                            var name = (string?)s.Attribute("name") ?? "";
                            return name == "EulaHtmlFile" || name == "FunctionalConsentFile" || name == "PrivacyPolicyFile";
                        })
                        .ToList();

                    foreach (var s in docStringsToFix)
                    {
                        var val = (string?)s.Attribute("value") ?? "";
                        if (!File.Exists(Path.Combine(extractDir, val)))
                        {
                            s.SetAttributeValue("value", "EULA.txt");
                        }
                    }

                    doc.Save(setupCfgPath);
                }
            }
            catch
            {
                // Non-fatal manifest patch error
            }
        }

        // 2. NVI2/presentations.cfg patching (anti-tracking URLs)
        var presCfgPath = Path.Combine(extractDir, "NVI2", "presentations.cfg");
        if (File.Exists(presCfgPath) && profile.DisableTelemetry)
        {
            try
            {
                var doc = XDocument.Load(presCfgPath);
                var root = doc.Root;
                var props = root?.Element("properties");
                if (props != null)
                {
                    foreach (var elem in props.Elements("string"))
                    {
                        var name = (string?)elem.Attribute("name");
                        if (name == "ProgressPresentationUrl" || name == "ProgressPresentationSelectedPackageUrl")
                        {
                            elem.SetAttributeValue("value", "");
                        }
                    }
                    doc.Save(presCfgPath);
                }
            }
            catch
            {
                // Non-fatal presentation patch error
            }
        }
    }

    public static void SafeDeleteDirectory(string path)
    {
        if (!Directory.Exists(path)) return;
        try
        {
            var dir = new DirectoryInfo(path);
            foreach (var fi in dir.EnumerateFiles("*", SearchOption.AllDirectories))
            {
                try { fi.Attributes = FileAttributes.Normal; } catch { }
            }
            dir.Delete(recursive: true);
        }
        catch (Exception ex)
        {
            AppLogger.LogBuild($"Warning: Failed to cleanly delete directory '{path}': {ex.Message}", "WARN");
        }
    }

    public static async Task<bool> ExtractPackageAsync(
        string packagePath,
        string destDir,
        IProgress<string>? statusCallback = null,
        CancellationToken ct = default)
    {
        Directory.CreateDirectory(destDir);

        // 1. Try 7-Zip CLI first (fastest and handles SFX stubs cleanly)
        var sevenZip = SettingsManager.Resolve7ZipPath();
        try
        {
            statusCallback?.Report("Extracting driver package contents (7-Zip)...");
            AppLogger.LogInstall($"Extracting '{packagePath}' to '{destDir}' via 7-Zip CLI...");

            var psi = new ProcessStartInfo
            {
                FileName = sevenZip,
                Arguments = $"x -y \"-o{destDir}\" \"{packagePath}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using var proc = Process.Start(psi);
            if (proc != null)
            {
                await proc.WaitForExitAsync(ct);
                if (proc.ExitCode == 0 && File.Exists(Path.Combine(destDir, "setup.exe")))
                {
                    return true;
                }
            }
        }
        catch (Exception ex)
        {
            AppLogger.LogInstall($"7-Zip extraction notice: {ex.Message}");
        }

        // 2. Fallback to SharpCompress
        try
        {
            statusCallback?.Report("Extracting package contents (SharpCompress)...");
            AppLogger.LogInstall($"Extracting '{packagePath}' via SharpCompress fallback...");
            await Task.Run(() =>
            {
                var readerOptions = new SharpCompress.Readers.ReaderOptions
                {
                    LookForHeader = true,
                    DisableCheckIncomplete = true
                };

                using var archive = SevenZipArchive.Open(packagePath, readerOptions);
                foreach (var entry in archive.Entries.Where(e => !e.IsDirectory))
                {
                    entry.WriteToDirectory(destDir, new ExtractionOptions
                    {
                        ExtractFullPath = true,
                        Overwrite = true
                    });
                }
            }, ct);

            return File.Exists(Path.Combine(destDir, "setup.exe"));
        }
        catch (Exception ex)
        {
            AppLogger.LogInstall($"SharpCompress extraction error: {ex.Message}");
            return false;
        }
    }

    public static string? FindAppxInArchive(string archivePath)
    {
        if (!File.Exists(archivePath)) return null;

        var sevenZip = SettingsManager.Resolve7ZipPath();
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = sevenZip,
                Arguments = $"l \"{archivePath}\" -r *.appx",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true
            };
            using var proc = Process.Start(psi);
            if (proc != null)
            {
                var output = proc.StandardOutput.ReadToEnd();
                proc.WaitForExit(5000);
                var match = Regex.Match(output, @"([a-zA-Z0-9_\.-]+\.appx(?:bundle)?)", RegexOptions.IgnoreCase);
                if (match.Success) return match.Groups[1].Value;
            }
        }
        catch { }

        return null;
    }

    public static async Task<bool> BuildSfxArchiveAsync(
        string sourceDir,
        string outputExePath,
        PackagingProfile profile,
        DriverPackage driverPackage,
        IProgress<string>? statusCallback = null,
        CancellationToken ct = default)
    {
        var sevenZipExe = SettingsManager.Resolve7ZipPath();
        var sfxStub = SettingsManager.Resolve7zSfxPath();

        if (string.IsNullOrEmpty(sfxStub) || !File.Exists(sfxStub))
        {
            AppLogger.LogBuild("7z.sfx stub not found. Unable to produce standalone SFX .exe.", "WARN");
            return false;
        }

        var tempPayload = Path.Combine(Path.GetTempPath(), $"nv_payload_{Guid.NewGuid():N}.7z");
        var tempExe = outputExePath + ".tmp";

        try
        {
            statusCallback?.Report("Compressing clean driver package (LZMA2)...");
            AppLogger.LogBuild($"Compressing '{sourceDir}' to '{tempPayload}' via 7-Zip CLI...");

            var psi = new ProcessStartInfo
            {
                FileName = sevenZipExe,
                Arguments = $"a -mx=5 -mmt=on \"{tempPayload}\" *",
                WorkingDirectory = sourceDir,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using var proc = Process.Start(psi);
            if (proc == null)
            {
                AppLogger.LogBuild("Failed to start 7-Zip process for compression.", "ERROR");
                return false;
            }

            await proc.WaitForExitAsync(ct);

            if (proc.ExitCode != 0 || !File.Exists(tempPayload))
            {
                var err = await proc.StandardError.ReadToEndAsync(ct);
                AppLogger.LogBuild($"7-Zip compression failed (Exit code {proc.ExitCode}): {err}", "ERROR");
                return false;
            }

            statusCallback?.Report("Combining 7z SFX stub and package payload into standalone executable...");
            AppLogger.LogBuild($"Creating standalone SFX executable: '{outputExePath}'...");

            var title = $"NVIDIA Graphics Driver v{driverPackage.Version} ({profile.DisplayName})";
            var configText = $";!@Install@!UTF-8!\r\nTitle=\"{title}\"\r\nRunProgram=\"setup.exe\"\r\n;!@InstallEnd@!\r\n";
            var configBytes = System.Text.Encoding.UTF8.GetBytes(configText);

            if (File.Exists(tempExe)) File.Delete(tempExe);

            await using (var outStream = File.Create(tempExe))
            {
                // 1. Write SFX stub (7z.sfx)
                await using (var sfxStream = File.OpenRead(sfxStub))
                {
                    await sfxStream.CopyToAsync(outStream, ct);
                }

                // 2. Write UTF-8 config text
                await outStream.WriteAsync(configBytes, ct);

                // 3. Write 7z payload
                await using (var payloadStream = File.OpenRead(tempPayload))
                {
                    await payloadStream.CopyToAsync(outStream, ct);
                }
            }

            if (File.Exists(outputExePath))
                File.Delete(outputExePath);

            File.Move(tempExe, outputExePath, overwrite: true);
            AppLogger.LogBuild($"Standalone SFX executable successfully generated at '{outputExePath}' ({new FileInfo(outputExePath).Length / (1024.0 * 1024.0):F1} MB)");
            return true;
        }
        catch (Exception ex)
        {
            AppLogger.LogBuildError("PackageBuilder.BuildSfxArchiveAsync", ex);
            return false;
        }
        finally
        {
            try { if (File.Exists(tempPayload)) File.Delete(tempPayload); } catch { }
            try { if (File.Exists(tempExe)) File.Delete(tempExe); } catch { }
        }
    }

    public static async Task<(bool success, string outputPath, string message)> BuildPackageAsync(
        PackagingProfile profile,
        DriverPackage driverPackage,
        IProgress<string>? statusCallback = null,
        IProgress<(long bytesRead, long totalBytes, double percentage)>? downloadProgress = null,
        CancellationToken ct = default)
    {
        AppLogger.LogBuild($"Starting driver package build for v{driverPackage.Version} ({driverPackage.DriverType}) with profile '{profile.Id}'");

        var rawDir = SettingsManager.ResolveRawPackagesDir();
        var cleanDir = SettingsManager.ResolveCleanPackagesDir();
        var downloadsDir = SettingsManager.ResolveDownloadsDir();

        var rawInstallerPath = driverPackage.LocalFilePath;

        // 1. Locate or download raw installer package into packages/raw/
        if (string.IsNullOrEmpty(rawInstallerPath) || !File.Exists(rawInstallerPath))
        {
            var fileName = $"NVIDIA_{driverPackage.Version}_x64_dch_{char.ToUpper(driverPackage.DriverType[0]) + driverPackage.DriverType[1..]}_Setup.exe";
            var rawCandidate = Path.Combine(rawDir, fileName);
            var legacyCandidate = Path.Combine(downloadsDir, fileName);

            if (File.Exists(rawCandidate))
            {
                rawInstallerPath = rawCandidate;
                AppLogger.LogBuild($"Found existing raw package in packages/raw: '{rawInstallerPath}'");
            }
            else if (File.Exists(legacyCandidate))
            {
                rawInstallerPath = legacyCandidate;
                AppLogger.LogBuild($"Found existing raw package in legacy packages root: '{rawInstallerPath}'");
            }
            else
            {
                if (string.IsNullOrEmpty(driverPackage.DownloadUrl))
                {
                    var err = "No download URL available for target package.";
                    AppLogger.LogBuild(err, "ERROR");
                    return (false, string.Empty, err);
                }

                rawInstallerPath = rawCandidate;
                statusCallback?.Report($"Downloading official NVIDIA {driverPackage.Version} package to raw cache...");
                AppLogger.LogBuild($"Downloading official package from: {driverPackage.DownloadUrl} -> {rawInstallerPath}");
                var dlOk = await DownloaderService.DownloadFileAsync(driverPackage.DownloadUrl, rawInstallerPath, downloadProgress, ct);
                if (!dlOk)
                {
                    var err = "Failed to download NVIDIA driver installer package.";
                    AppLogger.LogBuild(err, "ERROR");
                    return (false, string.Empty, err);
                }
            }
        }

        // 2. Unpack to temporary staging directory
        var stageDir = Path.Combine(Path.GetTempPath(), $"nv_stage_{Guid.NewGuid():N}");
        Directory.CreateDirectory(stageDir);

        try
        {
            statusCallback?.Report("Extracting package contents (7-Zip SFX)...");
            AppLogger.LogBuild($"Extracting raw installer '{rawInstallerPath}' to staging '{stageDir}'...");

            var extracted = await ExtractPackageAsync(rawInstallerPath, stageDir, statusCallback, ct);

            if (!extracted || !File.Exists(Path.Combine(stageDir, "setup.exe")))
            {
                var errMsg = "Extraction failed: setup.exe was not found in archive.";
                AppLogger.LogBuild(errMsg, "ERROR");
                return (false, string.Empty, errMsg);
            }

            // 3. Prune components
            statusCallback?.Report("Pruning unselected components and telemetry plugins...");
            AppLogger.LogBuild("Pruning unselected components...");
            PruneUnselectedComponents(stageDir, profile);

            // 4. Patch manifests
            statusCallback?.Report("Patching installer manifests (CleanInstall, anti-tracking)...");
            AppLogger.LogBuild("Patching manifests in setup.cfg and presentations.cfg...");
            PatchManifests(stageDir, profile);

            // 5. Build clean destination package in packages/clean/
            var cleanExeName = $"Clean_NVIDIA_{driverPackage.Version}_{char.ToUpper(driverPackage.DriverType[0]) + driverPackage.DriverType[1..]}_{profile.Id}_Setup.exe";
            var cleanExePath = Path.Combine(cleanDir, cleanExeName);

            statusCallback?.Report($"Building standalone clean package executable: {cleanExeName}...");
            AppLogger.LogBuild($"Building clean SFX package at '{cleanExePath}'...");

            var sfxSuccess = await BuildSfxArchiveAsync(stageDir, cleanExePath, profile, driverPackage, statusCallback, ct);

            string finalOutputPath;
            if (sfxSuccess && File.Exists(cleanExePath))
            {
                SafeDeleteDirectory(stageDir);
                finalOutputPath = cleanExePath;
                AppLogger.LogBuild($"Package executable built cleanly: '{finalOutputPath}'");
                statusCallback?.Report($"Package executable built cleanly: {cleanExeName}");
            }
            else
            {
                // Fallback to directory package if SFX creation failed
                AppLogger.LogBuild("Falling back to uncompressed directory package...", "WARN");
                var cleanDirName = $"Clean_NVIDIA_{driverPackage.Version}_{char.ToUpper(driverPackage.DriverType[0]) + driverPackage.DriverType[1..]}_{profile.Id}";
                var cleanDirPath = Path.Combine(cleanDir, cleanDirName);
                SafeDeleteDirectory(cleanDirPath);
                CopyDirectory(stageDir, cleanDirPath);
                SafeDeleteDirectory(stageDir);
                finalOutputPath = cleanDirPath;
                AppLogger.LogBuild($"Package directory built cleanly at '{finalOutputPath}'");
                statusCallback?.Report($"Package directory built cleanly in {cleanDirName}");
            }

            return (true, finalOutputPath, $"Driver package v{driverPackage.Version} built successfully.");
        }
        catch (Exception ex)
        {
            AppLogger.LogBuildError("PackageBuilder.BuildPackageAsync", ex);
            return (false, string.Empty, $"Package build error: {ex.Message}");
        }
    }

    /// <summary>
    /// Launches a visible interactive PowerShell console to execute the package build pipeline.
    /// Hooks to the PowerShell process, provides real-time progress feedback, and pauses on error.
    /// </summary>
    public static async Task<bool> BuildPackageViaPowerShellPromptAsync(
        PackagingProfile profile,
        DriverPackage driverPackage,
        CancellationToken ct = default)
    {
        var exePath = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "NVDriverInstaller.exe");
        var baseDir = AppContext.BaseDirectory;
        var tempScriptPath = Path.Combine(Path.GetTempPath(), $"build_pkg_{Guid.NewGuid():N}.ps1");

        var escapedExe = exePath.Replace("'", "''");
        var escapedBaseDir = baseDir.Replace("'", "''");
        var escapedProfile = profile.Id.Replace("'", "''");
        var escapedVersion = driverPackage.Version.Replace("'", "''");
        var escapedDriverType = driverPackage.DriverType.Replace("'", "''");
        var urlArg = !string.IsNullOrEmpty(driverPackage.DownloadUrl)
            ? $"--url '{driverPackage.DownloadUrl.Replace("'", "''")}'"
            : string.Empty;
        var localArg = !string.IsNullOrEmpty(driverPackage.LocalFilePath) && File.Exists(driverPackage.LocalFilePath)
            ? $"--local-file '{driverPackage.LocalFilePath.Replace("'", "''")}'"
            : string.Empty;

        var psScript = $@"
Set-Location '{escapedBaseDir}'
$host.UI.RawUI.WindowTitle = 'NVDriver Installer - Package Builder'
Clear-Host
Write-Host '================================================================' -ForegroundColor Cyan
Write-Host '          NVDriver Installer - Package Build Routine            ' -ForegroundColor Cyan
Write-Host '================================================================' -ForegroundColor Cyan
Write-Host ''
Write-Host '  Target Version:  v{escapedVersion} ({escapedDriverType})' -ForegroundColor Yellow
Write-Host '  Profile:         {profile.DisplayName.Replace("'", "''")}' -ForegroundColor Yellow
Write-Host '  Destination:     packages/clean' -ForegroundColor Yellow
Write-Host ''

& '{escapedExe}' build --profile '{escapedProfile}' --version '{escapedVersion}' --driver-type '{escapedDriverType}' {urlArg} {localArg} | Out-Host

$exitCode = $LASTEXITCODE
if ($null -eq $exitCode) {{ $exitCode = 0 }}
if ($exitCode -ne 0) {{
    Write-Host ''
    Write-Host '================================================================' -ForegroundColor Red
    Write-Host "" [!] Package compilation failed or was interrupted (Exit code: $exitCode)."" -ForegroundColor Red
    Write-Host '================================================================' -ForegroundColor Red
    Write-Host ''
    Read-Host 'Press Enter to close this window...'
    exit $exitCode
}} else {{
    Write-Host ''
    Write-Host '================================================================' -ForegroundColor Green
    Write-Host ' [+] Package compilation completed successfully!' -ForegroundColor Green
    Write-Host '================================================================' -ForegroundColor Green
    Write-Host ''
    Start-Sleep -Seconds 2
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

            using var proc = Process.Start(psi);
            if (proc != null)
            {
                await proc.WaitForExitAsync(ct);
                return proc.ExitCode == 0;
            }
        }
        catch (Exception ex)
        {
            AppLogger.LogBuildError("PackageBuilder.BuildPackageViaPowerShellPromptAsync", ex);
            return false;
        }
        finally
        {
            try { if (File.Exists(tempScriptPath)) File.Delete(tempScriptPath); } catch { }
        }

        return false;
    }
}
