using System.IO;
using NVDriverInstaller.Core.Models;
using Tomlyn;
using Tomlyn.Model;

namespace NVDriverInstaller.Core.Config;

/// <summary>
/// Scans and loads driver packaging and DDU profiles from TOML files in profiles/.
/// Provides fallback built-in presets if profile files are absent.
/// </summary>
public static class ProfileManager
{
    private static readonly List<PackagingProfile> _packagingProfiles = new();
    private static readonly List<DduProfile> _dduProfiles = new();
    private static bool _initialized;

    public static List<string> ParsingErrors { get; } = new();

    public static IReadOnlyList<PackagingProfile> PackagingProfiles
    {
        get
        {
            EnsureLoaded();
            return _packagingProfiles;
        }
    }

    public static IReadOnlyList<DduProfile> DduProfiles
    {
        get
        {
            EnsureLoaded();
            return _dduProfiles;
        }
    }

    public static void Reload()
    {
        _initialized = false;
        ParsingErrors.Clear();
        EnsureLoaded();
    }

    private static void EnsureLoaded()
    {
        if (_initialized) return;

        ParsingErrors.Clear();
        LoadPackagingProfiles();
        LoadDduProfiles();
        _initialized = true;
    }

    private static void LoadPackagingProfiles()
    {
        _packagingProfiles.Clear();
        var packagingDir = Path.Combine(AppContext.BaseDirectory, "profiles", "packaging");

        if (Directory.Exists(packagingDir))
        {
            foreach (var file in Directory.GetFiles(packagingDir, "*.toml"))
            {
                try
                {
                    var tomlContent = File.ReadAllText(file);
                    var model = Toml.ToModel(tomlContent);

                    var id = Path.GetFileNameWithoutExtension(file).ToLowerInvariant();
                    var nameVal = model.ContainsKey("name") ? model["name"]?.ToString() ?? id : id;
                    var descVal = model.ContainsKey("description") ? model["description"]?.ToString() ?? string.Empty : string.Empty;
                    var driverTypeVal = model.ContainsKey("driverType") ? model["driverType"]?.ToString()
                        : (model.ContainsKey("driver_type") ? model["driver_type"]?.ToString() : "desktop");
                    var driverBranchVal = model.ContainsKey("driverBranch") ? model["driverBranch"]?.ToString()
                        : (model.ContainsKey("driver_branch") ? model["driver_branch"]?.ToString() : "latest_dch");

                    var profile = new PackagingProfile
                    {
                        Id = id,
                        FileName = Path.GetFileName(file),
                        Name = nameVal,
                        Description = descVal,
                        DriverType = driverTypeVal ?? "desktop",
                        DriverBranch = driverBranchVal ?? "latest_dch"
                    };

                    if (model.TryGetValue("components", out var compObj) && compObj != null)
                    {
                        if (compObj is TomlArray comps)
                        {
                            profile.Components = comps.Select(c => c?.ToString() ?? string.Empty).Where(s => !string.IsNullOrEmpty(s)).ToList();
                        }
                        else if (compObj is TomlTable compTable)
                        {
                            if (compTable.TryGetValue("items", out var itemsObj) && itemsObj is TomlArray itemsArr)
                            {
                                profile.Components = itemsArr.Select(c => c?.ToString() ?? string.Empty).Where(s => !string.IsNullOrEmpty(s)).ToList();
                            }
                            else if (compTable.TryGetValue("list", out var listObj) && listObj is TomlArray listArr)
                            {
                                profile.Components = listArr.Select(c => c?.ToString() ?? string.Empty).Where(s => !string.IsNullOrEmpty(s)).ToList();
                            }
                            else
                            {
                                var compList = new List<string>();
                                var hasDisplay = compTable.ContainsKey("displayDriver") ? compTable["displayDriver"] as bool? : (compTable.ContainsKey("display_driver") ? compTable["display_driver"] as bool? : true);
                                var hasCpl = compTable.ContainsKey("controlPanel") ? compTable["controlPanel"] as bool? : (compTable.ContainsKey("control_panel") ? compTable["control_panel"] as bool? : false);
                                var hasPhysX = compTable.ContainsKey("physX") ? compTable["physX"] as bool? : (compTable.ContainsKey("physx") ? compTable["physx"] as bool? : false);
                                var hasAudio = compTable.ContainsKey("hdAudio") ? compTable["hdAudio"] as bool? : (compTable.ContainsKey("hd_audio") ? compTable["hd_audio"] as bool? : false);
                                var hasPlatform = compTable.ContainsKey("platformControllers") ? compTable["platformControllers"] as bool? : (compTable.ContainsKey("platform_controllers") ? compTable["platform_controllers"] as bool? : false);

                                if (hasDisplay != false) compList.Add("Display Driver (Required)");
                                if (hasCpl == true) compList.Add("NVIDIA Control Panel (AppX)");
                                if (hasPhysX == true) compList.Add("PhysX");
                                if (hasAudio == true) compList.Add("HD Audio (HDMI Audio)");
                                if (hasPlatform == true) compList.Add("NV Platform Controllers");
                                profile.Components = compList;
                            }
                        }
                    }

                    if (model.TryGetValue("tweaks", out var tweaksObj) && tweaksObj is TomlTable tweaks)
                    {
                        var dtVal = tweaks.ContainsKey("disableTelemetry") ? tweaks["disableTelemetry"] as bool? : (tweaks.ContainsKey("disable_telemetry") ? tweaks["disable_telemetry"] as bool? : null);
                        if (dtVal.HasValue) profile.DisableTelemetry = dtVal.Value;

                        var ciVal = tweaks.ContainsKey("cleanInstall") ? tweaks["cleanInstall"] as bool? : (tweaks.ContainsKey("clean_install") ? tweaks["clean_install"] as bool? : null);
                        if (ciVal.HasValue) profile.CleanInstall = ciVal.Value;

                        var uaVal = tweaks.ContainsKey("unattended") ? tweaks["unattended"] as bool? : null;
                        if (uaVal.HasValue) profile.Unattended = uaVal.Value;
                    }

                    _packagingProfiles.Add(profile);
                }
                catch (Exception ex)
                {
                    ParsingErrors.Add($"Packaging Profile '{Path.GetFileName(file)}': {ex.Message}");
                }
            }
        }

        // Add built-in fallbacks if none loaded
        if (_packagingProfiles.Count == 0)
        {
            _packagingProfiles.Add(new PackagingProfile
            {
                Id = "standard",
                FileName = "standard.toml",
                Name = "Desktop Standard",
                Description = "Standard desktop driver with PhysX, HD Audio, and Control Panel",
                DriverType = "desktop",
                Components = new() { "Display Driver (Required)", "NVIDIA Control Panel (AppX)", "PhysX", "HD Audio (HDMI Audio)" },
                DisableTelemetry = true,
                CleanInstall = true,
                Unattended = false
            });

            _packagingProfiles.Add(new PackagingProfile
            {
                Id = "minimal",
                FileName = "minimal.toml",
                Name = "Minimal Display Driver Only",
                Description = "Pure graphics display driver without audio, telemetry, or PhysX middleware",
                DriverType = "desktop",
                Components = new() { "Display Driver (Required)", "NVIDIA Control Panel (AppX)" },
                DisableTelemetry = true,
                CleanInstall = true,
                Unattended = false
            });
        }
    }

    private static void LoadDduProfiles()
    {
        _dduProfiles.Clear();
        var dduDir = Path.Combine(AppContext.BaseDirectory, "profiles", "ddu");

        if (Directory.Exists(dduDir))
        {
            foreach (var file in Directory.GetFiles(dduDir, "*.toml"))
            {
                try
                {
                    var tomlContent = File.ReadAllText(file);
                    var model = Toml.ToModel(tomlContent);

                    var id = Path.GetFileNameWithoutExtension(file).ToLowerInvariant();
                    var profile = new DduProfile
                    {
                        Id = id,
                        FileName = Path.GetFileName(file),
                        Name = model.ContainsKey("name") ? model["name"]?.ToString() ?? id : id,
                        Description = model.ContainsKey("description") ? model["description"]?.ToString() ?? string.Empty : string.Empty
                    };

                    if ((model.TryGetValue("cliFlags", out var flagsObj) || model.TryGetValue("cli_flags", out flagsObj)) && flagsObj is TomlArray flags)
                    {
                        profile.CliFlags = flags.Select(f => f?.ToString() ?? string.Empty).Where(s => !string.IsNullOrEmpty(s)).ToList();
                    }

                    if (model.ContainsKey("settings") && model["settings"] is TomlTable settingsTable)
                    {
                        foreach (var kvp in settingsTable)
                        {
                            if (kvp.Value != null)
                                profile.Settings[kvp.Key] = kvp.Value;
                        }
                    }

                    _dduProfiles.Add(profile);
                }
                catch (Exception ex)
                {
                    ParsingErrors.Add($"DDU Profile '{Path.GetFileName(file)}': {ex.Message}");
                }
            }
        }

        // Fallback default DDU profile
        if (_dduProfiles.Count == 0)
        {
            _dduProfiles.Add(new DduProfile
            {
                Id = "standard",
                FileName = "standard.toml",
                Name = "Standard Clean",
                Description = "Wipes NVIDIA driver, telemetry, GFE/App, and monitor caches while preserving PhysX and Audio Bus",
                CliFlags = new() { "-Silent", "-NoRestorePoint", "-PreventWinUpdate", "-CleanNvidia" }
            });

            _dduProfiles.Add(new DduProfile
            {
                Id = "deep",
                FileName = "deep.toml",
                Name = "Deep Purge",
                Description = "Total driver purge removing PhysX, Audio Bus, monitors, Vulkan, and all vendor leftovers",
                CliFlags = new() { "-Silent", "-NoRestorePoint", "-PreventWinUpdate", "-CleanNvidia", "-CleanAudioBus" }
            });
        }
    }

    public static PackagingProfile GetPackagingProfile(string? id)
    {
        EnsureLoaded();
        if (string.IsNullOrWhiteSpace(id))
            return _packagingProfiles.First();

        var match = _packagingProfiles.FirstOrDefault(p => p.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        return match ?? _packagingProfiles.First();
    }

    public static DduProfile GetDduProfile(string? id)
    {
        EnsureLoaded();
        if (string.IsNullOrWhiteSpace(id))
            return _dduProfiles.First();

        var match = _dduProfiles.FirstOrDefault(d => d.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        return match ?? _dduProfiles.First();
    }
}
