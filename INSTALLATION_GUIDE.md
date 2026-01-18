# 🐳 Docker BuildBox System — Windows Installation & Deployment Guide

## 📘 Introduction

This guide explains how to install and deploy **Docker BuildBox System** on Windows. It focuses on a streamlined setup for end users and optional developer paths.

**Key benefits**

* Faster container tooling
* Consistent environment setup
* Safer operations via guided workflows

---

## 🧭 Installation Types

Choose the path that best fits your needs:

| Installation Type                       | Description                                                                         | Link                                                                                                                                                  |
| --------------------------------------- | ----------------------------------------------------------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------- |
| 🟢 **Standard Installer (Recommended)** | Self-contained Windows desktop app with Start Menu and optional Desktop shortcut.   | See [Standard Installer](https://github.com/ReflexLevel0/DockerBuildboxSystem/edit/development/README.md#-standard-installer) |
| 📦 **Portable (No Installer)**          | Run from a published folder without installing; useful for controlled environments. | See [Portable Deploy](https://github.com/ReflexLevel0/DockerBuildboxSystem/DBS-217-installation-document/INSTALLATION_GUIDE.md#-portable-deploy)       |
| 🛠 **Developer (From Source)**          | Build and run from source for development or CI/CD.                                 | See [Install for Developers](https://github.com/ReflexLevel0/DockerBuildboxSystem/edit/development/README.md#-install-for-developers)                                                                                                 |

---

## 🧩 Overview

* **Application:** Docker BuildBox System (WPF, .NET 8, x64)
* **Installer:** Inno Setup–based; non-admin (per-user)
* **Target OS:** Windows 10 / 11 (x64)
* **Required:** Docker Desktop + Docker CLI

---

## 💻 System Requirements

* Windows 10 or Windows 11 (64-bit)
* x64 architecture
* Docker Desktop for Windows (Docker CLI available)
* Recommended: 8 GB RAM, 2 CPU cores, internet connectivity
* Disk space: ~300–500 MB (excluding container images)

**Notes by installation type**

* **Standard Installer:** No admin required; installs per-user
* **Portable:** Write access to target folder required
* **Developer:** .NET 8 SDK, Git, Inno Setup 6

---

## ⚠️ Before You Begin

* Install Docker Desktop for Windows and sign in if required
* Ensure Docker Engine can start (WSL2 backend recommended)
* Optional (developer): Install .NET 8 SDK, Git, Inno Setup

**Example (PowerShell)**

```powershell
# Install Docker Desktop
winget install --id Docker.DockerDesktop -e
# Optional: .NET 8 SDK
winget install --id Microsoft.DotNet.SDK.8 -e
# Optional: Inno Setup
winget install --id JRSoftware.InnoSetup -e
```

---

## 🚀 Installation Steps

### 🟢 Standard Installer

1. Download: Obtain the `DockerBuildBoxSystem_Setup_1.0.0.exe` from your distribution portal.
   - Result: Installer is available locally.
   - Check: File is signed/expected size; Windows SmartScreen allows running.

2. Launch: Double-click the installer.
   - Result: Modern wizard UI opens (per-user install).
   - Tip: No admin prompt; installation targets `%LocalAppData%`.

3. Accept Terms: Review EULA and privacy prompts.
   - Result: Proceed only after acknowledging warnings.
   - Check: Next button enabled after acknowledgment.

4. Docker Check: The installer warns if Docker is not detected.
   - Result: You can continue, but the app requires Docker to function.
   - Tip: If missing, install Docker Desktop before first use.

5. Choose Options: Optional desktop icon; confirm install directory.
   - Result: Files are copied; Start Menu shortcut created.
   - Check: Wizard completes with a success message.

6. Launch App: Click “Launch Docker BuildBox System” at the end.
   - Result: Application starts and loads main window.
   - Tip: If Docker isn’t running, start Docker Desktop first.

---

### 📦 Portable Deploy

1. Publish: Create a portable folder (self-contained) from source.
   - Result: A `publish\\win-x64` folder with all binaries.
   - Command:
   ```powershell
   dotnet publish .\src\DockerBuildBoxSystem.App -c Release -r win-x64 --self-contained true -o .\publish\win-x64
   ```
   - Check: `DockerBuildBoxSystem.App.exe` exists in `publish\\win-x64`.
   - Check (required files): Ensure these exist in `publish\\win-x64`:
     - `Config\appsettings.json`
     - `Config\config.json`
     - `Config\controls.json`
     - `Assets\icons\*`
     - If missing, fix the copy settings in `src/DockerBuildBoxSystem.App/DockerBuildBoxSystem.App.csproj` and republish.

2. Distribute: Zip and share `publish\\win-x64`.
   - Result: Recipients can extract and run without installer.
   - Tip: Place folder in a path without elevated permission requirements.

3. Run: Start `DockerBuildBoxSystem.App.exe`.
   - Result: App starts; config files load from `Config` subfolder.

---

### 🛠 Install for Developers

1. Clone repo: Get the source locally.
   - Result: Source code in a working directory.
   - Command:
   ```powershell
   git clone <[repo-url](https://github.com/ReflexLevel0/DockerBuildboxSystem)>
   cd DockerBuildBoxSystem
   ```

2. Restore & Build: Use .NET 8 SDK.
   - Result: Solution builds; WPF app compiles.
   - Commands:
   ```powershell
   dotnet restore DockerBuildBoxSystem.sln
   dotnet build src/DockerBuildBoxSystem.App/DockerBuildBoxSystem.App.csproj -c Debug
   dotnet run --project src/DockerBuildBoxSystem.App/DockerBuildBoxSystem.App.csproj
   ```

3. Build the Installer (optional): Use Inno Setup.
   - Result: A distributable `DockerBuildBoxSystem_Setup_1.0.0.exe`.
   - Commands:
   ```powershell
   # Publish self-contained app
   dotnet publish .\src\DockerBuildBoxSystem.App -c Release -r win-x64 --self-contained true -o .\publish\win-x64
   
   # Compile installer script (via ISCC or GUI)
   # Open and compile: installer/DockerBuildBoxSystem.iss
   ```
   - Reference: [installer](installer/DockerBuildBoxSystem.iss)
   - Before you publish: ensure config files are included in the publish output. The project already defines copy rules; if customizing, verify the following entries in `src/DockerBuildBoxSystem.App/DockerBuildBoxSystem.App.csproj`:
   ```xml
   <ItemGroup>
     <None Update="Config\appsettings.json">
       <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
       <CopyToPublishDirectory>PreserveNewest</CopyToPublishDirectory>
     </None>
     <None Update="Config\appsettings.Development.json">
       <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
       <CopyToPublishDirectory>PreserveNewest</CopyToPublishDirectory>
     </None>
     <None Update="Config\config.json">
       <CopyToOutputDirectory>Always</CopyToOutputDirectory>
       <CopyToPublishDirectory>Always</CopyToPublishDirectory>
     </None>
     <None Update="Config\controls.json">
       <CopyToOutputDirectory>Always</CopyToOutputDirectory>
       <CopyToPublishDirectory>Always</CopyToPublishDirectory>
     </None>
   </ItemGroup>
   <ItemGroup>
     <None Update="Assets\icons\*.*">
       <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
       <CopyToPublishDirectory>PreserveNewest</CopyToPublishDirectory>
     </None>
   </ItemGroup>
   ```
   - Verify after publish: Confirm the `Config` and `Assets\icons` folders exist under `.\publish\win-x64`.

---

## ✅ Verify Installation

- Start the app from Start Menu or Desktop icon.
- Ensure Docker Desktop is running; `docker info` returns engine details.
- In-app: Confirm initial screen loads; actions referencing containers respond without errors.


Commands (optional):
```powershell
# Verify Docker CLI
"$Env:ProgramFiles\\Docker\\Docker\\resources\\bin\\docker.exe" version
# Check engine
"$Env:ProgramFiles\\Docker\\Docker\\resources\\bin\\docker.exe" info
```

Expected results:
- App launches without errors.
- Docker CLI outputs version and engine details.

---

## ⚙️ Post Installation

* **Shortcuts:** Start Menu entry + optional desktop icon
* **Configuration:** Edit files in `Config` folder
* **Logs:** View via app or local log output

---

## 🔧 Configuration Options

* **App behavior:** `appsettings.json`
* **UI & controls:** `controls.json`
* **Core operations:** `config.json`

References:

* [appsettings.json](src/DockerBuildBoxSystem.App/Config/appsettings.json)
* [appsettings.Development.json](src/DockerBuildBoxSystem.App/Config/appsettings.Development.json)
* [config.json](src/DockerBuildBoxSystem.App/Config/config.json)
* [controls.json](src/DockerBuildBoxSystem.App/Config/controls.json)

---

## 🔄 Upgrade Options

* **Installer:** Run newer `DockerBuildBoxSystem_Setup_<version>.exe`
* **Portable:** Replace `publish\win-x64` folder
* **Developer:** Rebuild and repackage

Version source: `installer/DockerBuildBoxSystem.iss`

---

## 🧯 Troubleshooting


- Problem: “Docker Desktop not detected” warning during install.
  - Cause: Docker not installed or not on PATH.
  - Solution: Install Docker Desktop via `winget` and restart Docker.

- Problem: App launches but cannot communicate with Docker.
  - Cause: Docker Engine stopped or WSL2 backend issue.
  - Solution: Start Docker Desktop; ensure `docker info` succeeds.

- Problem: Execution blocked by SmartScreen.
  - Cause: Unsigned or unrecognized binary.
  - Solution: Use trusted distribution; run as allowed or contact the publisher.

- Problem: Missing configuration.
  - Cause: Config files not present or malformed.
  - Solution: Verify `Config` folder files; restore from repo sources.


---

## ➡️ Next Steps

* Explore container workflows
* Review domain services: [DockerBuildBoxSystem.Domain](src/DockerBuildBoxSystem.Domain)
* Customize configs and re-publish
* Contact support: **FER & MDU team**

---

## 📖 Definition of Terms

| Term           | Description                            |
| -------------- | -------------------------------------- |
| Docker Desktop | Windows GUI managing Docker Engine     |
| CLI            | Command Line Interface (`docker.exe`)  |
| Self-contained | .NET publish mode bundling runtime     |
| Inno Setup     | Installer framework used for packaging |

---