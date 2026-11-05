using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using NVDriverInstaller.Core.Config;
using NVDriverInstaller.Core.Models;

namespace NVDriverInstaller.Services;

public enum DduInstallState
{
    NotInstalled,
    Installed,
    UpdateAvailable
}

public class DduStatusInfo
{
    public DduInstallState State { get; set; } = DduInstallState.NotInstalled;
    public string? LocalVersion { get; set; }
    public string? RemoteVersion { get; set; }
    public bool IsUpdateAvailable => State == DduInstallState.UpdateAvailable;
    public string BadgeText { get; set; } = "Not Installed";
    public string BadgeBackground { get; set; } = "#2D1518";
    public string BadgeBorderBrush { get; set; } = "#5C2229";
    public string BadgeForeground { get; set; } = "#EF4444";
}

public static class DduManager
{
    private const string VersionTxtUrl = "https://www.wagnardsoft.com/DDU/currentversion2.txt";
    private const string DownloadBaseUrl = "https://download.wagnardsoft.com/DDU";

    private static string? _cachedRemoteVersion;

    public static string? CachedRemoteVersion => _cachedRemoteVersion;

    public static string GetDduExecutablePath()
    {
        return SettingsManager.ResolveDduPath();
    }

    public static bool IsInstalled()
    {
        var exe = GetDduExecutablePath();
        return !string.IsNullOrEmpty(exe) && File.Exists(exe);
    }

    public static string? GetLocalVersion()
    {
        var exe = GetDduExecutablePath();
        if (string.IsNullOrEmpty(exe) || !File.Exists(exe))
            return null;

        try
        {
            var info = FileVersionInfo.GetVersionInfo(exe);
            var ver = info.ProductVersion?.Trim() ?? info.FileVersion?.Trim();
            if (!string.IsNullOrEmpty(ver))
            {
                var match = Regex.Match(ver, @"\d+(\.\d+)+");
                if (match.Success)
                    return match.Value;
                return ver;
            }
        }
        catch
        {
            // Ignore
        }
        return null;
    }

    public static async Task<string?> CheckRemoteVersionAsync(bool forceRefresh = false, CancellationToken ct = default)
    {
        if (!forceRefresh && !string.IsNullOrEmpty(_cachedRemoteVersion))
            return _cachedRemoteVersion;

        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) NVDriverInstaller/1.0");

            var text = (await client.GetStringAsync(VersionTxtUrl, ct)).Trim();
            var match = Regex.Match(text, @"^\d+(\.\d+)+");
            if (match.Success)
            {
                _cachedRemoteVersion = match.Value;
                return _cachedRemoteVersion;
            }
        }
        catch
        {
            // Ignore remote check failure
        }
        return _cachedRemoteVersion;
    }

    public static bool IsVersionNewer(string localVersion, string remoteVersion)
    {
        if (string.IsNullOrWhiteSpace(remoteVersion) || string.IsNullOrWhiteSpace(localVersion))
            return false;

        var cleanLocal = Regex.Match(localVersion, @"\d+(\.\d+)+").Value;
        var cleanRemote = Regex.Match(remoteVersion, @"\d+(\.\d+)+").Value;

        if (string.IsNullOrEmpty(cleanLocal) || string.IsNullOrEmpty(cleanRemote))
            return false;

        if (Version.TryParse(cleanLocal, out var local) && Version.TryParse(cleanRemote, out var remote))
            return remote > local;

        var localParts = cleanLocal.Split('.').Select(p => int.TryParse(p, out var v) ? v : 0).ToArray();
        var remoteParts = cleanRemote.Split('.').Select(p => int.TryParse(p, out var v) ? v : 0).ToArray();
        var len = Math.Max(localParts.Length, remoteParts.Length);

        for (var i = 0; i < len; i++)
        {
            var l = i < localParts.Length ? localParts[i] : 0;
            var r = i < remoteParts.Length ? remoteParts[i] : 0;
            if (r > l) return true;
            if (r < l) return false;
        }

        return false;
    }

    public static bool IsUpdateAvailable()
    {
        if (!IsInstalled()) return false;
        var local = GetLocalVersion();
        var remote = _cachedRemoteVersion;
        if (string.IsNullOrEmpty(local) || string.IsNullOrEmpty(remote)) return false;
        return IsVersionNewer(local, remote);
    }

    public static DduStatusInfo GetStatusInfo(bool updateAvailable = false, string? availableVersion = null)
    {
        var info = new DduStatusInfo();
        if (!IsInstalled())
        {
            info.State = DduInstallState.NotInstalled;
            info.BadgeText = "Not Installed";
            info.BadgeBackground = "#2D1518";
            info.BadgeBorderBrush = "#5C2229";
            info.BadgeForeground = "#EF4444";
            return info;
        }

        var local = GetLocalVersion();
        info.LocalVersion = local;

        if (updateAvailable && !string.IsNullOrEmpty(availableVersion))
        {
            info.State = DduInstallState.UpdateAvailable;
            info.RemoteVersion = availableVersion;
            info.BadgeText = $"Update Available: v{availableVersion}";
            info.BadgeBackground = "#2D2214";
            info.BadgeBorderBrush = "#5C3B11";
            info.BadgeForeground = "#FBBF24";
        }
        else
        {
            info.State = DduInstallState.Installed;
            info.BadgeText = string.IsNullOrEmpty(local) ? "Installed" : $"Installed: v{local}";
            info.BadgeBackground = "#14261C";
            info.BadgeBorderBrush = "#1E472D";
            info.BadgeForeground = "#4ADE80";
        }

        return info;
    }

    /// <summary>
    /// Launches a visible PowerShell prompt to query Wagnardsoft for DDU updates.
    /// Displays version comparison and returns the result.
    /// </summary>
    public static (bool success, string? remoteVersion, bool isUpdateAvailable) CheckForUpdatesViaPowerShellPrompt()
    {
        var localVer = GetLocalVersion() ?? "unknown";
        var resultFile = Path.Combine(Path.GetTempPath(), $"ddu_check_result_{Guid.NewGuid():N}.json");
        var scriptFile = Path.Combine(Path.GetTempPath(), $"ddu_check_script_{Guid.NewGuid():N}.ps1");

        var script = GenerateDduUpdateCheckScript(localVer, resultFile);

        try
        {
            File.WriteAllText(scriptFile, script);

            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptFile}\"",
                UseShellExecute = true,
                CreateNoWindow = false,
                WindowStyle = ProcessWindowStyle.Normal
            };

            using var p = Process.Start(psi);
            if (p != null)
            {
                p.WaitForExit();
                if (File.Exists(resultFile))
                {
                    var json = File.ReadAllText(resultFile);
                    using var doc = JsonDocument.Parse(json);
                    var isNewer = doc.RootElement.TryGetProperty("isUpdateAvailable", out var n) && n.GetBoolean();
                    var remote = doc.RootElement.TryGetProperty("remoteVersion", out var r) ? r.GetString() : null;

                    if (!string.IsNullOrEmpty(remote))
                    {
                        _cachedRemoteVersion = remote;
                    }

                    return (true, remote, isNewer);
                }
            }
        }
        catch
        {
            return (false, null, false);
        }
        finally
        {
            try { if (File.Exists(scriptFile)) File.Delete(scriptFile); } catch { }
            try { if (File.Exists(resultFile)) File.Delete(resultFile); } catch { }
        }

        return (false, null, false);
    }

    private static string GenerateDduUpdateCheckScript(string localVersion, string resultFile)
    {
        var escapedResult = resultFile.Replace("'", "''");
        return $@"
$host.UI.RawUI.WindowTitle = 'NVDriver Installer - DDU Update Check'
Clear-Host
Write-Host '================================================================' -ForegroundColor Cyan
Write-Host '          NVDriver Installer - DDU Update Check                 ' -ForegroundColor Cyan
Write-Host '================================================================' -ForegroundColor Cyan
Write-Host ''
Write-Host '  Installed DDU Version: v{localVersion}' -ForegroundColor Gray
Write-Host ''
Write-Host '[*] Checking latest release from Wagnardsoft...' -ForegroundColor Yellow

[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12 -bor [Net.SecurityProtocolType]::Tls13

$remoteVer = $null
try {{
    $txt = (Invoke-RestMethod -Uri 'https://www.wagnardsoft.com/DDU/currentversion2.txt' -UserAgent 'Mozilla/5.0 (Windows NT 10.0; Win64; x64)' -TimeoutSec 10).Trim()
    if ($txt -match '^\d+(\.\d+)+$') {{
        $remoteVer = $txt
    }}
}} catch {{
    Write-Host ""  [!] Could not connect to Wagnardsoft: $($_.Exception.Message)"" -ForegroundColor DarkYellow
}}

$isNewer = $false
if ($remoteVer) {{
    Write-Host ""  Latest Available:      v$remoteVer"" -ForegroundColor Gray
    Write-Host ''
    
    try {{
        $vLocal = [version]('{localVersion}' -replace '[^\d.]', '')
        $vRemote = [version]($remoteVer -replace '[^\d.]', '')
        if ($vRemote -gt $vLocal) {{
            $isNewer = $true
        }}
    }} catch {{
        if ($remoteVer -ne '{localVersion}') {{
            $isNewer = $true
        }}
    }}

    if ($isNewer) {{
        Write-Host '================================================================' -ForegroundColor Yellow
        Write-Host ""  [UPDATE AVAILABLE] A newer version of DDU is available: v$remoteVer"" -ForegroundColor Yellow
        Write-Host ""  You can click 'Update DDU' in the app to download and upgrade."" -ForegroundColor Gray
        Write-Host '================================================================' -ForegroundColor Yellow
    }} else {{
        Write-Host '================================================================' -ForegroundColor Green
        Write-Host ""  [OK] Display Driver Uninstaller is up to date! (v{localVersion})"" -ForegroundColor Green
        Write-Host '================================================================' -ForegroundColor Green
    }}
}} else {{
    Write-Host ''
    Write-Host ""  [!] Could not verify remote version. You are currently on v{localVersion}."" -ForegroundColor DarkYellow
}}

$res = @{{
    localVersion = '{localVersion}'
    remoteVersion = $remoteVer
    isUpdateAvailable = $isNewer
}}
$res | ConvertTo-Json | Set-Content -Path '{escapedResult}' -Force

Write-Host ''
Start-Sleep -Seconds 2
exit 0
";
    }

    /// <summary>
    /// Launches a visible PowerShell console prompt to download, extract, and configure DDU.
    /// Provides live interactive feedback to the user and pauses on errors.
    /// </summary>
    public static bool InstallDduViaPowerShellPrompt(string targetDir)
    {
        var psScript = GenerateDduDownloadScript(targetDir);
        var tempScriptPath = Path.Combine(Path.GetTempPath(), $"install_ddu_{Guid.NewGuid():N}.ps1");

        try
        {
            File.WriteAllText(tempScriptPath, psScript);

            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{tempScriptPath}\"",
                UseShellExecute = true,
                CreateNoWindow = false,
                WindowStyle = ProcessWindowStyle.Normal
            };

            using var p = Process.Start(psi);
            if (p != null)
            {
                p.WaitForExit();
                if (p.ExitCode == 0)
                {
                    // Refresh cached remote version to current after update
                    _ = CheckRemoteVersionAsync(forceRefresh: true);
                    return true;
                }
            }
        }
        catch
        {
            return false;
        }
        finally
        {
            try
            {
                if (File.Exists(tempScriptPath))
                    File.Delete(tempScriptPath);
            }
            catch
            {
                // Ignore temp file cleanup failure
            }
        }

        return false;
    }

    private static string GenerateDduDownloadScript(string targetDir)
    {
        var escapedTarget = targetDir.Replace("'", "''");
        return $@"
$host.UI.RawUI.WindowTitle = 'NVDriver Installer - DDU Downloader'
Clear-Host
Write-Host '================================================================' -ForegroundColor Cyan
Write-Host '         NVDriver Installer - DDU Setup & Downloader            ' -ForegroundColor Cyan
Write-Host '================================================================' -ForegroundColor Cyan
Write-Host ''

# Force TLS 1.2 / TLS 1.3
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12 -bor [Net.SecurityProtocolType]::Tls13

# 1. Fetch latest version
Write-Host '[1/3] Checking latest DDU release from Wagnardsoft...' -ForegroundColor Yellow
$version = '18.1.6.2'
try {{
    $remote = (Invoke-RestMethod -Uri 'https://www.wagnardsoft.com/DDU/currentversion2.txt' -UserAgent 'Mozilla/5.0 (Windows NT 10.0; Win64; x64)' -TimeoutSec 10).Trim()
    if ($remote -match '^\d+(\.\d+)+$') {{
        $version = $remote
        Write-Host ""  -> Target Version: v$version"" -ForegroundColor Green
    }}
}} catch {{
    Write-Host ""  -> Note: Remote check timed out, using verified release v$version"" -ForegroundColor DarkYellow
}}

# 2. Download SFX
$targetDir = '{escapedTarget}'
$downloadUrl = ""https://download.wagnardsoft.com/DDU/DDU%20v$version.exe""
$tempSfx = Join-Path ([System.IO.Path]::GetTempPath()) ""DDU_v$version.exe""

Write-Host ''
Write-Host ""[2/3] Downloading DDU v$version installer package..."" -ForegroundColor Yellow
Write-Host ""  -> Source: $downloadUrl"" -ForegroundColor Gray

try {{
    Invoke-WebRequest -Uri $downloadUrl -OutFile $tempSfx -UserAgent 'Mozilla/5.0 (Windows NT 10.0; Win64; x64)' -UseBasicParsing
    $fileSize = (Get-Item $tempSfx).Length
    $mbSize = [math]::Round($fileSize / 1MB, 2)
    Write-Host ""  -> Download successful! ($mbSize MB)"" -ForegroundColor Green
}} catch {{
    Write-Host ""  [ERROR] Failed to download DDU: $_"" -ForegroundColor Red
    Write-Host ''
    Read-Host 'Press Enter to exit...'
    exit 1
}}

# 3. Extract and stage
Write-Host ''
Write-Host '[3/3] Extracting portable DDU package...' -ForegroundColor Yellow
$tempExtract = Join-Path ([System.IO.Path]::GetTempPath()) ('ddu_unpack_' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $tempExtract -Force | Out-Null

try {{
    $sfxProc = Start-Process -FilePath $tempSfx -ArgumentList ""-y -o`""$tempExtract`"""" -Wait -PassThru -NoNewWindow
    if ($sfxProc.ExitCode -ne 0) {{
        throw ""7-Zip self-extractor returned exit code $($sfxProc.ExitCode)""
    }}

    $extractedFolder = Get-ChildItem -Path $tempExtract -Directory | Select-Object -First 1
    if (-not $extractedFolder) {{
        $extractedFolder = Get-Item $tempExtract
    }}

    if (-not (Test-Path $targetDir)) {{
        New-Item -ItemType Directory -Path $targetDir -Force | Out-Null
    }}

    # Preserve existing Settings.xml if present
    $settingsFile = Join-Path $targetDir 'settings\Settings.xml'
    $backupSettings = $null
    if (Test-Path $settingsFile) {{
        $backupSettings = Get-Content -Path $settingsFile -Raw
    }}

    Copy-Item -Path (Join-Path $extractedFolder.FullName '*') -Destination $targetDir -Recurse -Force

    if ($backupSettings) {{
        $settingsDir = Join-Path $targetDir 'settings'
        if (-not (Test-Path $settingsDir)) {{ New-Item -ItemType Directory -Path $settingsDir -Force | Out-Null }}
        Set-Content -Path $settingsFile -Value $backupSettings -Force
    }}

    $exeCheck = Join-Path $targetDir 'Display Driver Uninstaller.exe'
    if (-not (Test-Path $exeCheck)) {{
        # Also check subfolder
        $subCheck = Join-Path $targetDir 'DDU\Display Driver Uninstaller.exe'
        if (-not (Test-Path $subCheck)) {{
            throw 'Display Driver Uninstaller.exe not found in extracted output.'
        }}
    }}

    Write-Host ""  -> Extracted successfully to: $targetDir"" -ForegroundColor Green
}} catch {{
    Write-Host ""  [ERROR] Extraction failed: $_"" -ForegroundColor Red
    Write-Host ''
    Read-Host 'Press Enter to exit...'
    exit 1
}} finally {{
    Remove-Item -Path $tempSfx -Force -ErrorAction SilentlyContinue
    Remove-Item -Path $tempExtract -Recurse -Force -ErrorAction SilentlyContinue
}}

Write-Host ''
Write-Host '================================================================' -ForegroundColor Green
Write-Host '       Display Driver Uninstaller (DDU) is ready for use!       ' -ForegroundColor Green
Write-Host '================================================================' -ForegroundColor Green
Write-Host ''
Start-Sleep -Seconds 2
exit 0
";
    }

    public static async Task<(bool success, string message)> DownloadAndInstallLatestDduAsync(
        IProgress<string>? statusCallback = null,
        CancellationToken ct = default)
    {
        var targetDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "tools", "ddu"));
        var ok = await Task.Run(() => InstallDduViaPowerShellPrompt(targetDir), ct);

        if (ok && IsInstalled())
            return (true, "Display Driver Uninstaller is ready.");

        return (false, "Failed to complete DDU installation.");
    }

    public static bool ApplyProfileSettings(DduProfile profile)
    {
        var exePath = GetDduExecutablePath();
        if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
            return false;

        var dduRoot = Path.GetDirectoryName(exePath) ?? AppContext.BaseDirectory;
        var settingsDir = Path.Combine(dduRoot, "settings");
        Directory.CreateDirectory(settingsDir);
        var settingsXml = Path.Combine(settingsDir, "Settings.xml");

        try
        {
            XDocument doc;
            XElement root;
            if (File.Exists(settingsXml))
            {
                doc = XDocument.Load(settingsXml);
                root = doc.Root ?? new XElement("DisplayDriverUninstaller", new XAttribute("Version", "18.1.6.2"));
            }
            else
            {
                root = new XElement("DisplayDriverUninstaller", new XAttribute("Version", "18.1.6.2"));
                doc = new XDocument(root);
            }

            var settingsElem = root.Element("Settings");
            if (settingsElem == null)
            {
                settingsElem = new XElement("Settings");
                root.Add(settingsElem);
            }

            var canonicalMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["RemoveNvidiaDirs"] = "RemoveNvidiaDirs",
                ["Remove3DTVPlay"] = "Remove3DTVPlay",
                ["RemoveGFE"] = "RemoveGFE",
                ["RemoveNVBROADCAST"] = "RemoveNVBROADCAST",
                ["RemoveNVCP"] = "RemoveNVCP",
                ["RemoveNvidiaCache"] = "RemoveNvidiaCache",
                ["RemovePhysX"] = "RemovePhysX",
                ["RemoveAudioBus"] = "RemoveAudioBus",
                ["RemoveMonitors"] = "RemoveMonitors",
                ["RemoveVulkan"] = "RemoveVulkan",
                ["SaveLogs"] = "SaveLogs",
                ["PreventWinUpdate"] = "PreventWinUpdate",
                ["CreateRestorePoint"] = "CreateRestorePoint",
                ["ShowOffer"] = "ShowOffer",
                ["EnableSafeModeDialog"] = "EnableSafeModeDialog"
            };

            foreach (var kvp in profile.Settings)
            {
                var canonicalKey = canonicalMap.TryGetValue(kvp.Key, out var cName) ? cName : kvp.Key;
                var elem = settingsElem.Elements()
                    .FirstOrDefault(e => e.Name.LocalName.Equals(canonicalKey, StringComparison.OrdinalIgnoreCase));

                if (elem == null)
                {
                    elem = new XElement(canonicalKey);
                    settingsElem.Add(elem);
                }

                if (kvp.Value is bool b)
                    elem.Value = b ? "True" : "False";
                else
                    elem.Value = kvp.Value?.ToString() ?? "";
            }

            doc.Save(settingsXml);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static async Task<(bool success, string message)> ExecuteSilentCleanAsync(
        DduProfile profile,
        CancellationToken ct = default)
    {
        var exePath = GetDduExecutablePath();
        if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
            return (false, $"DDU executable not found at: {exePath}");

        ApplyProfileSettings(profile);

        var flags = string.Join(" ", profile.CliFlags);
        var psi = new ProcessStartInfo(exePath, flags)
        {
            UseShellExecute = false,
            CreateNoWindow = true
        };

        try
        {
            using var process = Process.Start(psi);
            if (process == null)
                return (false, "Failed to launch DDU process.");

            await process.WaitForExitAsync(ct);
            return (process.ExitCode == 0, $"DDU completed with exit code {process.ExitCode}.");
        }
        catch (Exception ex)
        {
            return (false, $"DDU execution error: {ex.Message}");
        }
    }
}
