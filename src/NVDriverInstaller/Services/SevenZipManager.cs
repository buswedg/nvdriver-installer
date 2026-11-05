using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using NVDriverInstaller.Core.Config;

namespace NVDriverInstaller.Services;

public enum SevenZipInstallState
{
    NotInstalled,
    Installed,
    UpdateAvailable,
    MissingSfxStub
}

public class SevenZipStatusInfo
{
    public SevenZipInstallState State { get; set; } = SevenZipInstallState.NotInstalled;
    public string? LocalVersion { get; set; }
    public string? RemoteVersion { get; set; }
    public bool IsInstalled => State == SevenZipInstallState.Installed || State == SevenZipInstallState.UpdateAvailable || State == SevenZipInstallState.MissingSfxStub;
    public bool HasSfxStub => State != SevenZipInstallState.MissingSfxStub && State != SevenZipInstallState.NotInstalled;
    public bool IsUpdateAvailable => State == SevenZipInstallState.UpdateAvailable;
    public string BadgeText { get; set; } = "Not Installed";
    public string BadgeBackground { get; set; } = "#2D1518";
    public string BadgeBorderBrush { get; set; } = "#5C2229";
    public string BadgeForeground { get; set; } = "#EF4444";
}

public static class SevenZipManager
{
    private const string GitHubApiLatest = "https://api.github.com/repos/ip7z/7zip/releases/latest";
    private const string SevenZipDownloadPage = "https://www.7-zip.org/download.html";

    private static string? _cachedRemoteVersion;

    public static string? CachedRemoteVersion => _cachedRemoteVersion;

    public static string GetExecutablePath()
    {
        return SettingsManager.Resolve7ZipPath();
    }

    public static string? GetSfxPath()
    {
        return SettingsManager.Resolve7zSfxPath();
    }

    public static bool IsInstalled()
    {
        var exe = GetExecutablePath();
        return !string.IsNullOrEmpty(exe) && File.Exists(exe);
    }

    public static bool HasSfxStub()
    {
        var sfx = GetSfxPath();
        return !string.IsNullOrEmpty(sfx) && File.Exists(sfx);
    }

    public static string? GetLocalVersion()
    {
        var exe = GetExecutablePath();
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

        // 1. Try 7-zip.org scraping (official primary canonical source, zero rate limits)
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(6) };
            client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64)");

            var html = await client.GetStringAsync(SevenZipDownloadPage, ct);
            var match = Regex.Match(html, @"Download 7-Zip (\d+\.\d+)");
            if (match.Success)
            {
                _cachedRemoteVersion = match.Groups[1].Value;
                return _cachedRemoteVersion;
            }
        }
        catch
        {
            // Fall through to GitHub releases API
        }

        // 2. Fallback to GitHub releases API
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(6) };
            client.DefaultRequestHeaders.Add("User-Agent", "NVDriverInstaller/1.0");

            var json = await client.GetStringAsync(GitHubApiLatest, ct);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("tag_name", out var tagElem))
            {
                var tag = tagElem.GetString();
                if (!string.IsNullOrEmpty(tag))
                {
                    var match = Regex.Match(tag, @"\d+(\.\d+)+");
                    if (match.Success)
                    {
                        _cachedRemoteVersion = match.Value;
                        return _cachedRemoteVersion;
                    }
                }
            }
        }
        catch
        {
            // Remote check failed
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

    public static SevenZipStatusInfo GetStatusInfo(bool updateAvailable = false, string? availableVersion = null)
    {
        var info = new SevenZipStatusInfo();
        if (!IsInstalled())
        {
            info.State = SevenZipInstallState.NotInstalled;
            info.BadgeText = "Not Installed";
            info.BadgeBackground = "#2D1518";
            info.BadgeBorderBrush = "#5C2229";
            info.BadgeForeground = "#EF4444";
            return info;
        }

        var local = GetLocalVersion();
        info.LocalVersion = local;

        if (!HasSfxStub())
        {
            info.State = SevenZipInstallState.MissingSfxStub;
            info.BadgeText = string.IsNullOrEmpty(local) ? "Missing SFX Stub" : $"v{local} (Missing SFX Stub)";
            info.BadgeBackground = "#2D2214";
            info.BadgeBorderBrush = "#5C3B11";
            info.BadgeForeground = "#FBBF24";
            return info;
        }

        if (updateAvailable && !string.IsNullOrEmpty(availableVersion))
        {
            info.State = SevenZipInstallState.UpdateAvailable;
            info.RemoteVersion = availableVersion;
            info.BadgeText = $"Update Available: v{availableVersion}";
            info.BadgeBackground = "#2D2214";
            info.BadgeBorderBrush = "#5C3B11";
            info.BadgeForeground = "#FBBF24";
        }
        else
        {
            info.State = SevenZipInstallState.Installed;
            info.BadgeText = string.IsNullOrEmpty(local) ? "Installed" : $"Installed: v{local}";
            info.BadgeBackground = "#14261C";
            info.BadgeBorderBrush = "#1E472D";
            info.BadgeForeground = "#4ADE80";
        }

        return info;
    }

    /// <summary>
    /// Launches a visible PowerShell prompt to check for 7-Zip updates and return comparison results.
    /// </summary>
    public static (bool success, string? remoteVersion, bool isUpdateAvailable) CheckForUpdatesViaPowerShellPrompt()
    {
        var localVer = GetLocalVersion() ?? "unknown";
        var resultFile = Path.Combine(Path.GetTempPath(), $"7z_check_result_{Guid.NewGuid():N}.json");
        var scriptFile = Path.Combine(Path.GetTempPath(), $"7z_check_script_{Guid.NewGuid():N}.ps1");

        var script = GenerateUpdateCheckScript(localVer, resultFile);

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
                if (p.ExitCode == 0 && File.Exists(resultFile))
                {
                    var json = File.ReadAllText(resultFile);
                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;
                    var remote = root.TryGetProperty("remoteVersion", out var rElem) ? rElem.GetString() : null;
                    var isNewer = root.TryGetProperty("isUpdateAvailable", out var uElem) && uElem.GetBoolean();

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
            // Ignore
        }
        finally
        {
            try { if (File.Exists(scriptFile)) File.Delete(scriptFile); } catch { }
            try { if (File.Exists(resultFile)) File.Delete(resultFile); } catch { }
        }

        return (false, null, false);
    }

    private static string GenerateUpdateCheckScript(string localVersion, string resultFile)
    {
        var escapedResult = resultFile.Replace("'", "''");
        return $@"
$host.UI.RawUI.WindowTitle = 'NVDriver Installer - 7-Zip Update Check'
Clear-Host
Write-Host '================================================================' -ForegroundColor Cyan
Write-Host '          NVDriver Installer - 7-Zip Update Check               ' -ForegroundColor Cyan
Write-Host '================================================================' -ForegroundColor Cyan
Write-Host ''
Write-Host '  Installed 7-Zip Version: v{localVersion}' -ForegroundColor Gray
Write-Host ''
Write-Host '[*] Checking latest release from GitHub / 7-zip.org...' -ForegroundColor Yellow

[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12 -bor [Net.SecurityProtocolType]::Tls13

$remoteVer = $null
try {{
    $headers = @{{ 'User-Agent' = 'NVDriverInstaller/1.0' }}
    $resp = Invoke-RestMethod -Uri '{GitHubApiLatest}' -Headers $headers -TimeoutSec 10
    if ($resp.tag_name -match '\d+(\.\d+)+') {{
        $remoteVer = $Matches[0]
    }}
}} catch {{
    try {{
        $html = (Invoke-WebRequest -Uri '{SevenZipDownloadPage}' -UserAgent 'Mozilla/5.0' -TimeoutSec 10 -UseBasicParsing).Content
        if ($html -match 'Download 7-Zip (\d+\.\d+)') {{
            $remoteVer = $Matches[1]
        }}
    }} catch {{
        Write-Host ""  [!] Could not connect to update servers: $($_.Exception.Message)"" -ForegroundColor DarkYellow
    }}
}}

$isNewer = $false
if ($remoteVer) {{
    Write-Host ""  Latest Available:        v$remoteVer"" -ForegroundColor Gray
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
        Write-Host ""  [UPDATE AVAILABLE] A newer version of 7-Zip is available: v$remoteVer"" -ForegroundColor Yellow
        Write-Host ""  You can click 'Update 7-Zip' in the app to download and upgrade."" -ForegroundColor Gray
        Write-Host '================================================================' -ForegroundColor Yellow
    }} else {{
        Write-Host '================================================================' -ForegroundColor Green
        Write-Host ""  [OK] 7-Zip is up to date! (v{localVersion})"" -ForegroundColor Green
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
    /// Launches a visible PowerShell prompt to download, extract, and configure 7-Zip &amp; SFX modules.
    /// </summary>
    public static bool InstallSevenZipViaPowerShellPrompt(string targetDir)
    {
        var psScript = GenerateDownloadScript(targetDir);
        var tempScriptPath = Path.Combine(Path.GetTempPath(), $"install_7z_{Guid.NewGuid():N}.ps1");

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
            try { if (File.Exists(tempScriptPath)) File.Delete(tempScriptPath); } catch { }
        }

        return false;
    }

    private static string GenerateDownloadScript(string targetDir)
    {
        var escapedTarget = targetDir.Replace("'", "''");
        return $@"
$host.UI.RawUI.WindowTitle = 'NVDriver Installer - 7-Zip Downloader'
Clear-Host
Write-Host '================================================================' -ForegroundColor Cyan
Write-Host '        NVDriver Installer - 7-Zip Tooling Setup               ' -ForegroundColor Cyan
Write-Host '================================================================' -ForegroundColor Cyan
Write-Host ''

[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12 -bor [Net.SecurityProtocolType]::Tls13

# 1. Fetch latest version
Write-Host '[1/3] Resolving latest 7-Zip release...' -ForegroundColor Yellow
$version = '26.05'
try {{
    $headers = @{{ 'User-Agent' = 'NVDriverInstaller/1.0' }}
    $resp = Invoke-RestMethod -Uri '{GitHubApiLatest}' -Headers $headers -TimeoutSec 10
    if ($resp.tag_name -match '\d+(\.\d+)+') {{
        $version = $Matches[0]
        Write-Host ""  -> Target Version: v$version"" -ForegroundColor Green
    }}
}} catch {{
    Write-Host ""  -> Using default release: v$version"" -ForegroundColor DarkYellow
}}

$verDigits = $version -replace '\.', ''
$downloadUrl = ""https://github.com/ip7z/7zip/releases/download/$version/7z$verDigits-x64.exe""
$targetDir = '{escapedTarget}'
$tempInstaller = Join-Path ([System.IO.Path]::GetTempPath()) ""7z$verDigits-x64.exe""

Write-Host ''
Write-Host ""[2/3] Downloading 7-Zip v$version official installer package..."" -ForegroundColor Yellow
Write-Host ""  -> Source: $downloadUrl"" -ForegroundColor Gray

try {{
    Invoke-WebRequest -Uri $downloadUrl -OutFile $tempInstaller -UserAgent 'Mozilla/5.0 (Windows NT 10.0; Win64; x64)' -UseBasicParsing
    $fileSize = (Get-Item $tempInstaller).Length
    $mbSize = [math]::Round($fileSize / 1MB, 2)
    Write-Host ""  -> Download successful! ($mbSize MB)"" -ForegroundColor Green
}} catch {{
    Write-Host ""  [ERROR] Failed to download 7-Zip: $_"" -ForegroundColor Red
    Write-Host ''
    Read-Host 'Press Enter to exit...'
    exit 1
}}

# 3. Extract and stage into target directory
Write-Host ''
Write-Host ""[3/3] Staging 7-Zip binaries into: $targetDir..."" -ForegroundColor Yellow

if (-not (Test-Path $targetDir)) {{
    New-Item -ItemType Directory -Path $targetDir -Force | Out-Null
}}

try {{
    # 7-Zip installer executable is internally an archive containing 7z.exe, 7z.dll, 7z.sfx, 7zCon.sfx.
    # Native Windows tar.exe (libarchive) extracts these directly without requiring administrative elevation.
    $tarExe = Join-Path $env:SystemRoot 'System32\tar.exe'
    $extractedViaTar = $false

    if (Test-Path $tarExe) {{
        Write-Host '  -> Extracting CLI & SFX binaries via native Windows tar...' -ForegroundColor Gray
        $tarProc = Start-Process -FilePath $tarExe -ArgumentList ""-xf `""$tempInstaller`"" -C `""$targetDir`"" 7z.exe 7z.dll 7z.sfx 7zCon.sfx"" -Wait -PassThru -NoNewWindow
        if ($tarProc.ExitCode -eq 0 -and (Test-Path (Join-Path $targetDir '7z.exe'))) {{
            $extractedViaTar = $true
        }}
    }}

    if (-not $extractedViaTar) {{
        # Fallback to direct installer execution if tar is not available
        Write-Host '  -> Running installer setup...' -ForegroundColor Gray
        $instProc = Start-Process -FilePath $tempInstaller -ArgumentList ""/S /D=$targetDir"" -Wait -PassThru
        if ($instProc.ExitCode -ne 0) {{
            throw ""7-Zip extraction returned exit code $($instProc.ExitCode)""
        }}
    }}

    $exe = Join-Path $targetDir '7z.exe'
    $sfx = Join-Path $targetDir '7z.sfx'

    if (-not (Test-Path $exe)) {{
        throw ""7z.exe was not created in $targetDir""
    }}

    Write-Host ""  -> Verified 7z.exe: OK"" -ForegroundColor Green

    # Verify or fetch SFX modules if missing
    if (-not (Test-Path $sfx)) {{
        $extraUrl = ""https://github.com/ip7z/7zip/releases/download/$version/7z$verDigits-extra.7z""
        $tempExtra = Join-Path ([System.IO.Path]::GetTempPath()) ""7z$verDigits-extra.7z""
        try {{
            Write-Host ""  -> Downloading 7-Zip Extra SFX modules package..."" -ForegroundColor Gray
            Invoke-WebRequest -Uri $extraUrl -OutFile $tempExtra -UserAgent 'Mozilla/5.0' -UseBasicParsing
            if (Test-Path $tempExtra) {{
                if (Test-Path $tarExe) {{
                    & $tarExe -xf $tempExtra -C $targetDir 7z.sfx 7zCon.sfx
                }} else {{
                    & $exe e $tempExtra ""-o$targetDir"" ""7z.sfx"" ""7zCon.sfx"" -y | Out-Null
                }}
                Remove-Item -Path $tempExtra -Force -ErrorAction SilentlyContinue
            }}
        }} catch {{
            Write-Host ""  -> Note: SFX modules package optional download skipped: $_"" -ForegroundColor DarkGray
        }}
    }}

    if (Test-Path $sfx) {{
        Write-Host ""  -> Verified 7z.sfx (SFX module): OK"" -ForegroundColor Green
    }} else {{
        Write-Host ""  -> Warning: 7z.sfx not found in installer root"" -ForegroundColor DarkYellow
    }}

    Write-Host ''
    Write-Host '================================================================' -ForegroundColor Green
    Write-Host '  [OK] 7-Zip tooling installed and ready for standalone packaging!' -ForegroundColor Green
    Write-Host '================================================================' -ForegroundColor Green
}} catch {{
    Write-Host ""  [ERROR] Staging failed: $_"" -ForegroundColor Red
    Write-Host ''
    Read-Host 'Press Enter to exit...'
    exit 1
}} finally {{
    if (Test-Path $tempInstaller) {{
        Remove-Item -Path $tempInstaller -Force -ErrorAction SilentlyContinue
    }}
}}

Write-Host ''
Start-Sleep -Seconds 2
exit 0
";
    }

    /// <summary>
    /// Launches an interactive PowerShell console with 7z.exe executed and ready for commands.
    /// </summary>
    public static void OpenSevenZip()
    {
        var exe = GetExecutablePath();
        if (!File.Exists(exe)) return;

        var dir = Path.GetDirectoryName(exe) ?? AppContext.BaseDirectory;
        var escapedExe = exe.Replace("'", "''");
        var escapedDir = dir.Replace("'", "''");

        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoExit -ExecutionPolicy Bypass -Command \"$host.UI.RawUI.WindowTitle = 'NVDriver Installer - 7-Zip CLI Console'; Set-Alias 7z '{escapedExe}' -Scope Global -Force; $env:PATH = '{escapedDir};' + $env:PATH; & '{escapedExe}'; Write-Host '`n[7-Zip Interactive PowerShell Ready] Type 7z commands below (e.g. 7z --help, 7z l <archive>, 7z x <archive>):' -ForegroundColor Cyan\"",
            WorkingDirectory = dir,
            UseShellExecute = true,
            WindowStyle = ProcessWindowStyle.Normal
        };

        try
        {
            Process.Start(psi);
        }
        catch { }
    }

    public static void OpenToolsFolder()
    {
        var exe = GetExecutablePath();
        var dir = File.Exists(exe) ? Path.GetDirectoryName(exe) : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "tools", "7z"));
        if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = dir,
                    UseShellExecute = true
                });
            }
            catch { }
        }
    }
}
