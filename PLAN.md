# NVDriver Installer (`nvdriver-installer`)
## Modern Windows 11 Architecture, Migration Plan & Improvement Roadmap

> **Status:** Architecture Approved & Implementation Ready  
> **Target Framework:** .NET 10 (`net10.0-windows`)  
> **UI Engine:** Modern Windows 11 Fluent Design (`Wpf.Ui`) with Mica backdrop & Software Rendering Fallback  
> **CLI Engine:** Unified Headless Console Subsystem for script automation (`quick-trigger`, CI/CD)  
> **Binary Target:** Portable standalone suite (`NVDriverInstaller.exe`) with local `tools/` and `profiles/`  
> **Primary Goal:** Transform `nvdriver-installer` from an interpreted Python developer script into an indestructible, self-contained native Windows utility ready for Step 20 of a fresh bare-metal Windows installation.

---

## 1. Executive Summary & Why Port to C#?

In the previous Python version, `nvdriver-installer` functioned well in an established developer environment, but suffered from a **fundamental architectural paradox**:

```
┌────────────────────────────────────────────────────────────────────────┐
│ The "Fresh Install" Catch-22 in the Current Python App:               │
│                                                                        │
│ 1. You clean install Windows 11.                                       │
│ 2. Installing clean GPU drivers is Step 20 (before developer tools).   │
│ 3. But to run nvdriver-installer, you need Python 3.11+, Poetry, and  │
│    a virtualenv with 40+ packages installed.                           │
│ 4. If the Safe Mode reboot encounters path or interpreter glitches,    │
│    the workflow is stranded midway without graphics drivers.           │
└────────────────────────────────────────────────────────────────────────┘
```

Porting `nvdriver-installer` to **C# with .NET 10** resolves this completely. The output is a **portable, standalone Windows application** that:
- Runs immediately on an offline, bare-metal Windows 11 desktop from a USB thumb drive.
- Reboots into Windows Safe Mode without relying on fragile script associations or environment variables.
- Survives DDU tearing out the GPU display driver without crashing, thanks to WPF's `SoftwareOnly` rendering mode.
- Delivers an authentic Windows 11 look with Mica materials, rounded Fluent cards, and dark theme.
- Matches the native companion pattern established by [`quick-trigger`](file:///mnt/c/DropDrive/Projects/quick-trigger).

---

## 2. Key Improvements & Intentional Deviations from Python

### Improvement 1: Zero-Prerequisite Standalone Application
* **Previous (Python):** Required Python 3.11–3.14 on PATH, Poetry, pip packages (Textual, Rich, PyYAML, tomli), and virtual environment staging (`env/`).
* **C# Improvement:** Single self-contained application. Zero external runtimes or interpreters required.

### Improvement 2: Authentic Windows 11 Fluent UI (`Wpf.Ui`) + Mica
* **Previous (Python):** Terminal-only Textual TUI.
* **C# Improvement:** Modern Windows 11 Fluent GUI using `Wpf.Ui`:
  - Genuine Windows 11 Mica backdrop, rounded Fluent cards, native navigation drawer.
  - Safe Mode Resilience: Automatically sets `RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;` so the UI runs entirely on CPU/GDI, remaining 100% stable during GPU driver uninstallation and in Safe Mode.

### Improvement 3: Single Source of Truth Configuration
* **Previous (Python):** Fragmented across `config.dist.toml`, `config.toml`, and loose YAML files. Required manual file copying before first launch.
* **C# Improvement:** 
  - **App Settings (UI-Managed):** General settings (DDU path, auto-reboot, network quarantine, telemetry disable) are configured directly in the UI Settings page and auto-saved to local `settings.json`. Built-in defaults guarantee zero-setup first launch.
  - **Profiles (Modular TOML):** Driver stripping and DDU cleanup presets are modular TOML files in `profiles/packaging/*.toml` and `profiles/ddu/*.toml`. The UI scans this folder dynamically; users can drop new `.toml` profiles in without recompiling.

### Improvement 4: True Portable App with Local `tools/` Directory
* **Previous (Python):** Downloaded DDU into arbitrary folders and left unpacked files behind.
* **C# Improvement:** Designed as a self-contained portable folder:
  - DDU sits in `tools/ddu/Display Driver Uninstaller.exe` by default.
  - User can override the path via the in-app Settings UI if they maintain a central tools share.
  - If `tools/ddu/` is empty, the UI provides a one-click button to download and unpack the latest official release from Wagnardsoft directly into `tools/ddu/`.

### Improvement 5: Direct Win32 & WMI APIs (Eliminate PowerShell Shell-Outs)
* **Previous (Python):** Spawned child processes running PowerShell cmdlets (`Get-NetAdapter`, `Disable-NetAdapter`).
* **C# Improvement:** Uses native C# `System.Net.NetworkInformation` and Windows WMI (`root\StandardCimv2:MSFT_NetAdapter`). Direct, sub-millisecond execution with zero process-spawn overhead.

### Improvement 6: Resilient Multi-Boot State Machine
* **Previous (Python):** Relied on `RunOnce` launching `launch.py` via Python.
* **C# Improvement:** `RunOnce` points directly to `NVDriverInstaller.exe --resume`. If any unexpected failure occurs during Safe Mode DDU, a failsafe watchdog automatically clears the Safe Mode BCD boot flag so the user is never stuck in a boot loop.

---

## 3. Project Structure

```
nvdriver-installer/
├── .gitignore                         # Fleet-compliant gitignore
├── .gitattributes                     # Strict LF / CRLF line endings
├── README.md                          # Plain-English humanized documentation
├── LICENSE                            # MIT License
├── build.bat                          # One-click release publishing script
├── profiles/
│   ├── packaging/
│   │   ├── standard.toml              # Desktop standard preset (Display + PhysX + HD Audio + Control Panel)
│   │   ├── notebook.toml              # Mobile preset (Standard + NVPCF platform controllers)
│   │   ├── minimal.toml               # Display Driver only, maximum debloating
│   │   └── deep.toml                  # Pure barebones display driver, silent express
│   └── ddu/
│       ├── standard.toml              # Desktop standard clean (wipes telemetry, preserves audio/physx)
│       ├── notebook.toml              # Safe clean for hybrid graphics
│       ├── minimal.toml               # Preserves monitor EDID configurations
│       └── deep.toml                  # Total driver and vendor purge
├── tools/
│   └── ddu/                           # Default portable home for DDU (.gitignored)
│       └── .keep
└── src/
    └── NVDriverInstaller/
        ├── NVDriverInstaller.csproj   # net10.0-windows, Wpf.Ui, Tomlyn
        ├── App.xaml                   # Application container & styling
        ├── App.xaml.cs                # Entry point, SoftwareOnly rendering, argument dispatcher
        ├── MainWindow.xaml            # Root Fluent window with NavigationView
        ├── MainWindow.xaml.cs         # Navigation handling & Mica initialization
        │
        ├── Core/                      # Models, configuration, and helpers
        │   ├── Models/                # DriverPackage, GpuInfo, PackagingProfile, DduProfile, WorkflowState, AppSettings
        │   ├── Config/                # SettingsManager (UI-managed), ProfileManager (TOML loader)
        │   └── Win32/                 # P/Invoke definitions (BCD, RunOnce, SetupAPI, NVAPI)
        │
        ├── Services/                  # Business logic & hardware interaction
        │   ├── SystemInspector.cs     # Active GPU, driver conversion, chassis type, Safe Mode detection
        │   ├── NvidiaCatalogClient.cs # Queries https://gpu.me/v1/index2.json
        │   ├── DownloaderService.cs   # HTTP streaming with progress callbacks
        │   ├── PackageBuilder.cs      # Pruning, XML manifest patching, SFX generation
        │   ├── DduManager.cs          # DDU path resolution, Settings.xml injection, silent execution
        │   ├── NetworkQuarantineService.cs # Adapter snapshot, isolation & restoration
        │   ├── BcdBootManager.cs      # SafeBoot Minimal configuration, RunOnce hooks, reboot scheduling
        │   └── WorkflowCoordinator.cs # Multi-boot state machine (Prepare -> SafeModeDdu -> Install -> Finish)
        │
        ├── ViewModels/                # MVVM ViewModels
        │   ├── DashboardViewModel.cs  # System overview, GPU card, quick actions
        │   ├── InstallViewModel.cs    # Clean install wizard, Safe Mode flow
        │   ├── BuildViewModel.cs      # Standalone driver package debloater
        │   ├── UtilitiesViewModel.cs  # Network quarantine, DDU updater, manual reboot
        │   └── SettingsViewModel.cs   # UI settings persistence
        │
        └── Views/Pages/               # WPF UI Views
            ├── DashboardPage.xaml     # Overview hero card & system status
            ├── InstallPage.xaml       # Step-by-step upgrade wizard
            ├── BuildPage.xaml         # Catalog browser & package generator
            ├── UtilitiesPage.xaml     # Diagnostics, network adapter table, DDU status
            └── SettingsPage.xaml      # Visual settings editor (DDU path, toggles)
```

---

## 4. Implementation Plan & Milestones

- [x] **Milestone 1:** Finalize Architecture & Specification (`PLAN.md`).
- [ ] **Milestone 2:** Scaffolding & Fleet Compliance (`.gitignore`, `.gitattributes`, `LICENSE`, `README.md`, `build.bat`).
- [ ] **Milestone 3:** TOML Profile Presets (`profiles/packaging/*.toml`, `profiles/ddu/*.toml`).
- [ ] **Milestone 4:** Core Models & Config Management (`AppSettings`, `SettingsManager`, `ProfileManager`).
- [ ] **Milestone 5:** Hardware, Boot & OS Services (`SystemInspector`, `BcdBootManager`, `NetworkQuarantineService`).
- [ ] **Milestone 6:** Driver Engine & DDU Orchestration (`NvidiaCatalogClient`, `PackageBuilder`, `DduManager`, `WorkflowCoordinator`).
- [ ] **Milestone 7:** Modern Fluent GUI (`Wpf.Ui` Window, Navigation, Pages & MVVM ViewModels).
- [ ] **Milestone 8:** Headless CLI Subsystem & Verification.
