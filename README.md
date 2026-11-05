# NVDriver Installer

[![Platform](https://img.shields.io/badge/Platform-Windows%2010%20%7C%2011%20(x64)-blue.svg)](#)
[![Runtime](https://img.shields.io/badge/.NET-10.0%20WindowsDesktop-purple.svg)](#)
[![Architecture](https://img.shields.io/badge/Architecture-x64-informational.svg)](#)
[![License](https://img.shields.io/badge/License-MIT-green.svg)](#)

NVDriver Installer is a lightweight, portable Windows utility that automates debloated NVIDIA GPU driver packaging and clean multi-boot upgrades. It fetches official DCH releases, prunes telemetry and unwanted subcomponents, provisions the offline NVIDIA Control Panel (AppX), isolates network adapters to prevent Windows Update driver interference, and guides your system through a Safe Mode Display Driver Uninstaller (DDU) cleanup.

---

## Runtime Requirements

The precompiled release executable (`NVDriverInstaller.exe`, ~1.8 MB) is distributed as a lightweight, framework-dependent single-file binary.

### Prerequisites

To run the precompiled release, install the **.NET 10 Windows Desktop Runtime**:

- **Winget:**
  ```powershell
  winget install Microsoft.DotNet.DesktopRuntime.10
  ```
- **Manual Download:**
  Download the **.NET Desktop Runtime 10.0 (x64)** installer from [Microsoft .NET Downloads](https://dotnet.microsoft.com/download/dotnet/10.0).

> **Note:** If you need a completely standalone executable that runs without installing the .NET runtime, compile it in self-contained mode. See [Building from Source](#building-from-source).

---

## Features

- **Modern Windows 11 UI:** Built with `Wpf.Ui` featuring native Mica materials, Fluent cards, and dark theme.
- **Safe Mode Resilience:** Forces software rendering (`RenderMode.SoftwareOnly`) so the application remains 100% stable when GPU display drivers are uninstalled or running on the Microsoft Basic Display Adapter.
- **Automated Multi-Boot Workflow:** Coordinates network quarantine, Windows BCD SafeBoot Minimal configuration, Winlogon RunOnce automation, silent Display Driver Uninstaller (DDU) execution, return reboot to normal Windows, clean driver setup, and network restoration.
- **Modular Packaging Profiles:** Customizable TOML presets (`profiles/packaging/*.toml`) select exactly which driver components to keep (Display Driver, NVCPL AppX, PhysX, HD Audio) and strip telemetry tracking endpoints and telemetry plugins.
- **Standalone SFX Compiler:** Packages debloated drivers into single-file self-extracting executables (`Clean_NVIDIA_*_Setup.exe`) using portable 7-Zip CLI tooling and SFX modules.
- **Offline NVIDIA Control Panel Provisioning:** Extracts and installs the modern NVIDIA Control Panel AppX package offline via PowerShell and DISM, avoiding Microsoft Store delays while offline.
- **Network Adapter Quarantine:** Identifies physical network adapters via WMI/CIM and PowerShell, safely disabling them during driver replacement to block Windows Update interference, and restoring them automatically.
- **Pre-Flight Simulation Mode:** Validates file integrity, component manifests, tooling status, and displays the full automated multi-boot execution plan without making system modifications.
- **Dual-Mode Operation:** Run interactively via the Fluent desktop window or headlessly through command-line verbs for automated provisioning pipelines.

---

## Quick Start

### Running the App
Double-click `NVDriverInstaller.exe`. When performing Clean Upgrades, Safe Mode scheduling, or Network Quarantine, launch with Administrator privileges.

### Command-Line Usage
```cmd
:: Inspect graphics hardware, driver version, and host form factor
NVDriverInstaller.exe inspect

:: Inspect or toggle physical network adapter quarantine
NVDriverInstaller.exe network status
NVDriverInstaller.exe network disable
NVDriverInstaller.exe network restore

:: Build a clean, debloated driver package without installing
NVDriverInstaller.exe build --profile standard --version 621.01

:: Run silent DDU driver wipe using a specific profile
NVDriverInstaller.exe ddu --profile standard

:: Initiate automated Safe Mode clean upgrade workflow
NVDriverInstaller.exe install --profile standard --clean --auto-reboot
```

---

## Directory Layout

```text
NVDriverInstaller/
├── NVDriverInstaller.exe         # Application executable (~1.8 MB)
├── settings.json                 # In-app preferences (auto-generated)
├── profiles/
│   ├── packaging/                # Driver debloating presets (TOML)
│   │   ├── standard.toml         # Desktop standard preset (PhysX + HD Audio + Control Panel)
│   │   ├── notebook.toml         # Mobile preset (Standard + Platform Controllers)
│   │   ├── minimal.toml          # Minimal display driver only (no audio/physx)
│   │   └── deep.toml             # Deep minimal preset with silent unattended install
│   └── ddu/                      # DDU cleanup presets (TOML)
│       ├── standard.toml         # Desktop standard clean (preserves audio bus and PhysX)
│       ├── notebook.toml         # Mobile clean for hybrid graphics
│       ├── minimal.toml          # Minimal wipe (preserves monitors, audio, PhysX)
│       └── deep.toml             # Deep purge (total vendor wipe)
├── packages/
│   ├── raw/                      # Downloaded official NVIDIA installer packages
│   └── clean/                    # Debloated driver packages & SFX archives
├── tools/
│   ├── ddu/                      # Portable Display Driver Uninstaller
│   └── 7z/                       # Portable 7-Zip CLI & SFX modules
└── logs/
    ├── app.log                   # General application log
    ├── build.log                 # Package extraction & compression log
    └── install.log               # Driver installer execution log
```

---

## Configuration & Profile Reference

Both driver packaging and DDU cleanup workflows are configured via standardized TOML profile files located in `profiles/packaging/` and `profiles/ddu/`.

Profiles follow standard camelCase parameter formatting. CLI parameters are defined as arrays, and configurable subsets are grouped under explicit section headers (`[components]`, `[tweaks]`, and `[settings]`).

```
                              PROFILES MATRIX
  ┌─────────────────────────────────┬─────────────────────────────────┐
  │      Packaging Profiles         │          DDU Profiles           │
  │     (profiles/packaging/)       │        (profiles/ddu/)          │
  ├─────────────────────────────────┼─────────────────────────────────┤
  │ standard.toml (Default Desktop) │ standard.toml (Default Desktop) │
  │ notebook.toml (Default Mobile)  │ notebook.toml (Mobile Hybrid)   │
  │ minimal.toml  (Display + AppX)  │ minimal.toml  (Light Purge)     │
  │ deep.toml     (Barebones Silent)│ deep.toml     (Total Deep Purge)│
  └─────────────────────────────────┴─────────────────────────────────┘
```

---

### 1. Packaging Profiles (`profiles/packaging/*.toml`)

Packaging profiles determine which driver components are retained and what tweaks are injected during package building:

#### Example: `standard.toml`
```toml
name = "Desktop Standard"
description = "Desktop driver with PhysX, HD Audio, and NVIDIA Control Panel"

[components]
displayDriver = true
controlPanel = true
physX = true
hdAudio = true
platformControllers = false

[tweaks]
disableTelemetry = true
cleanInstall = true
unattended = false
```

#### Complete Packaging Profile Parameter Reference

| Parameter / Key | Type | Default | Description |
| :--- | :--- | :--- | :--- |
| **`name`** | string | *file id* | Friendly display name shown in GUI dropdowns and simulation summaries. |
| **`description`** | string | `""` | Detailed summary describing the target profile use case. |
| **`driverType`** *(optional)* | string | *auto-detect* | Optional target override (`"desktop"` or `"notebook"`). When omitted, auto-detected from host hardware. |
| **`driverBranch`** *(optional)* | string | `"latest_dch"` | Optional channel tag. |
| **`[components]`** | section | — | Boolean flags defining driver components to retain. |
| `displayDriver` | boolean | `true` | Retains core graphics display driver (`Display.Driver/`). Mandatory. |
| `controlPanel` | boolean | `false` | Retains modern offline AppX control panel and Win32 applet (`NvCpl/`). |
| `physX` | boolean | `false` | Retains NVIDIA PhysX physics runtime engine (`PhysX/`). |
| `hdAudio` | boolean | `false` | Retains HDMI and DisplayPort HD Audio driver (`HDAudio/`). |
| `platformControllers`| boolean | `false` | Retains mobile platform controllers and PPC firmware (`NVPCF/`, `PPC/`). |
| `items` *(legacy)* | string[] | `[]` | Optional string list for backwards compatibility. See [Supported Component Names](#supported-component-names). |
| **`[tweaks]`** | section | — | Installer manifest and engine optimizations. |
| `disableTelemetry` | boolean | `true` | Strips telemetry plugins (`*telemetry*`), clears advertising URLs in `presentations.cfg`, and cleans `setup.cfg`. |
| `cleanInstall` | boolean | `true` | Injects `<bool name="clean" value="true" />` into `setup.cfg` options. |
| `unattended` | boolean | `false` | Configures unattended express silent installation (`-s -clean`) without installer wizard prompts. |

#### Supported Component Names

When configuring `[components].items`, the following string tokens are recognized:
- `"Display Driver (Required)"` — Core graphics display driver (mandatory).
- `"NVIDIA Control Panel (AppX)"` or `"Legacy Control Panel"` — Retains `NvCpl/` and triggers offline AppX provisioning.
- `"PhysX"` — NVIDIA PhysX physics middleware.
- `"HD Audio (HDMI Audio)"` — HDMI and DisplayPort audio driver.
- `"NV Platform Controllers"` — Mobile platform controller firmware for laptops (retains `NVPCF/` and `PPC/`).

All unselected components (such as `NvApp`, `ShadowPlay`, `NvBackend`, `NvContainer`, `NvTelemetry`, `FrameViewSDK`, `NvDLISR`, `NvVAD`, and `MSVCRT`) are automatically pruned from the package.

---

### 2. DDU Profiles (`profiles/ddu/*.toml`)

DDU profiles define silent command-line arguments and configuration options passed to Display Driver Uninstaller (`Display Driver Uninstaller.exe`). Metadata (`name`, `description`) is explicitly spaced from functional CLI arguments (`cliFlags`) and DDU registry settings (`[settings]`):

#### Example: `standard.toml`
```toml
name = "Desktop Standard Clean"
description = "Wipes NVIDIA driver, telemetry, GFE/App, and monitor caches while preserving PhysX and Audio Bus"

cliFlags = [
    "-Silent",
    "-NoRestorePoint",
    "-PreventWinUpdate",
    "-CleanNvidia"
]

[settings]
removeNvidiaDirs = true
remove3dtvPlay = true
removeGfe = true
removeNvBroadcast = true
removeNvcp = true
removeNvidiaCache = true
removePhysx = false
removeAudioBus = false
removeMonitors = true
removeVulkan = true
saveLogs = true
preventWinUpdate = true
createRestorePoint = false
showOffer = false
enableSafeModeDialog = false
```

#### Complete DDU Profile Parameter Reference

| Parameter / Key | Type | Default | Description |
| :--- | :--- | :--- | :--- |
| **`name`** | string | *file id* | Friendly display name shown in GUI dropdowns and simulation logs. |
| **`description`** | string | `""` | Brief description of the cleanup level. |
| **`cliFlags`** | string[] | `[]` | Command-line switches passed to DDU. Recommended: `["-Silent", "-NoRestorePoint", "-PreventWinUpdate", "-CleanNvidia"]`. |
| **`[settings]`** | section | — | Configuration key-value pairs written directly to DDU's `Settings.xml`. |
| `removeNvidiaDirs` | boolean | `true` | Deletes residual NVIDIA directories from `Program Files` and `ProgramData`. |
| `remove3dtvPlay` | boolean | `true` | Removes 3DTV Play legacy configurations. |
| `removeGfe` | boolean | `true` | Cleans GeForce Experience and NVIDIA App residual registry keys and folders. |
| `removeNvBroadcast` | boolean | `true` | Removes residual NVIDIA Broadcast applet configurations. |
| `removeNvcp` | boolean | `true` | Purges Control Panel registration to ensure clean reinstallation. |
| `removeNvidiaCache` | boolean | `true` | Cleans DirectX, OpenGL, and Vulkan GPU shader cache directories. |
| `removePhysx` | boolean | `false` | Purges NVIDIA PhysX runtime libraries (set `true` in `deep.toml`). |
| `removeAudioBus` | boolean | `false` | Purges HD Audio bus device instances (preserve `false` to avoid re-enumeration issues). |
| `removeMonitors` | boolean | `true` | Cleans disconnected/stale monitor EDID entries from the Windows registry. |
| `removeVulkan` | boolean | `true` | Removes stale Vulkan runtime registrations from the driver store. |
| `saveLogs` | boolean | `true` | Automatically writes DDU session log files. |
| `preventWinUpdate` | boolean | `true` | Configures local policy to prevent Windows Update from installing generic drivers during cleanup. |
| `createRestorePoint`| boolean | `false` | Creates a Windows System Restore Point prior to wiping. |
| `showOffer` | boolean | `false` | Suppresses external sponsor offers in DDU. |
| `enableSafeModeDialog`| boolean| `false` | Suppresses interactive Safe Mode prompt dialogs during headless runs. |

---

### 3. Application Preferences (`settings.json`)

Application preferences are managed automatically via the in-app Settings UI and stored in `settings.json`:

| Setting Key | Type | Default | Description |
| :--- | :--- | :--- | :--- |
| `ddu_path` | string | `"./tools/ddu/Display Driver Uninstaller.exe"` | Relative or absolute path to DDU executable. |
| `seven_zip_path` | string | `"./tools/7z/7z.exe"` | Path to portable 7-Zip CLI compiler. |
| `download_cache_path` | string | `"./packages"` | Root storage directory for packages (`raw/` and `clean/`). |
| `default_desktop_packaging_profile` | string | `"standard"` | Default packaging profile loaded for Desktop form factors. |
| `default_notebook_packaging_profile` | string | `"notebook"` | Default packaging profile loaded for Notebook form factors. |
| `default_ddu_profile` | string | `"standard"` | Default DDU cleanup profile applied during Clean Upgrades. |
| `preferred_install_pathway` | string | `"clean"` | Default workflow pathway (`"clean"` for Safe Mode DDU, `"standard"` for in-place). |
| `auto_reboot_to_safe_mode` | boolean | `true` | Automatically arms BCD SafeBoot and restarts into Safe Mode. |
| `auto_reboot_after_safe_mode` | boolean | `true` | Automatically restarts from Safe Mode back into normal Windows after purge. |
| `auto_reboot_after_install` | boolean | `true` | Automatically initiates final reboot to finalize graphics driver setup. |
| `quarantine_network` | boolean | `true` | Temporarily disables physical network adapters to prevent Windows Update interference. |
| `unattended_install` | boolean | `true` | Executes NVIDIA setup with `-s -clean` silently. |
| `install_nvcpl_appx` | boolean | `true` | Installs offline NVIDIA Control Panel AppX via PowerShell/DISM. |
| `dry_run_mode` | boolean | `false` | Simulates all actions in logs without executing changes. |
| `reboot_delay_seconds` | integer | `5` | Countdown timer before reboot commands execute. |
| `safe_mode_runonce_name` | string | `"*NVDriverInstallerResume"` | Registry RunOnce entry for Safe Mode resumption. |
| `normal_mode_runonce_name`| string | `"NVDriverInstallerResume"` | Registry RunOnce entry for Normal Mode return. |

---

## Building from Source

### Prerequisites
- Windows 10 / 11 (x64)
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)

### Build Script
Run the automated build script from the repository root:

```cmd
build.bat
```

To clean intermediate build artifacts before compiling:
```cmd
build.bat --clean
```

The compiled single-file binary, configuration profiles, and portable directory structure are staged in `release\`.

---

## License

MIT License. See [LICENSE](LICENSE) for details.
