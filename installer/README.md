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
| 🟢 **Standard Installer (Recommended)** | Self-contained Windows desktop app with Start Menu and optional Desktop shortcut.   | See [Standard Installer](#-standard-installer) |
| 📦 **Portable (No Installer)**          | Run from a published folder without installing; useful for controlled environments. | See [Portable Deploy](#-portable-deploy)       |
| 🛠 **Developer (From Source)**          | Build and run from source for development or CI/CD.                                 | See [For Developers](#-install-for-developers)                                                                                                 |

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

1. Download: Obtain the `DockerBuildBoxSystem_Setup.exe` from your distribution portal.
2. Launch: Double-click the installer.
3. Accept Terms: Review EULA and privacy prompts.
4. Choose Options: Optional desktop icon; confirm install directory.
5. Launch App: Click “Launch Docker BuildBox System” at the end.

---

### 📦 Portable Deploy

1. Publish: Create a portable folder (self-contained) from source.
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
3. Run: Start `DockerBuildBoxSystem.App.exe`.

---

### 🛠 Install for Developers

1. Clone repo: Get the source locally.
   ```powershell
   git clone https://github.com/ReflexLevel0/DockerBuildboxSystem
   cd DockerBuildBoxSystem
   ```
2. Restore & Build: Use .NET 8 SDK.
   ```powershell
   dotnet restore DockerBuildBoxSystem.sln
   dotnet build src/DockerBuildBoxSystem.App/DockerBuildBoxSystem.App.csproj -c Debug
   dotnet run --project src/DockerBuildBoxSystem.App/DockerBuildBoxSystem.App.csproj
   ```
3. Build the Installer (optional): Use Inno Setup.
   ```powershell
   dotnet publish .\src\DockerBuildBoxSystem.App -c Release -r win-x64 --self-contained true -o .\publish\win-x64
   ```
---

## ✅ Verify Installation

- Start the app from Start Menu or Desktop icon.
- Ensure Docker Desktop is running; `docker info` returns engine details.
- In-app: Confirm initial screen loads; actions referencing containers respond without errors.

---

## ⚙️ Post Installation

* **Shortcuts:** Start Menu entry and optional desktop icon
* **Configuration:** Edit files in `Config` folder
* **Logs:** View via app or local log output

---

## 🔧 Configuration Options

* **App behavior:** [`appsettings.json`](https://github.com/ReflexLevel0/DockerBuildboxSystem/blob/main/src/DockerBuildBoxSystem.App/Config/appsettings.json)
* **UI & controls:** [`controls.json`](https://github.com/ReflexLevel0/DockerBuildboxSystem/blob/main/src/DockerBuildBoxSystem.App/Config/controls.json)
* **Core operations:** [`config.json`](https://github.com/ReflexLevel0/DockerBuildboxSystem/blob/main/src/DockerBuildBoxSystem.App/Config/config.json)

---

## 🧯 Troubleshooting

| Problem                                                  | Cause                                                     | Solution                                                                         |
| -------------------------------------------------------- | --------------------------------------------------------- | -------------------------------------------------------------------------------- |
| **“Docker Desktop not detected” warning during install** | Docker is not installed or not available on `PATH`        | Install Docker Desktop using `winget` and restart Docker                         |
| **App launches but cannot communicate with Docker**      | Docker Engine is stopped or there is a WSL2 backend issue | Start Docker Desktop and verify that `docker info` runs successfully             |
| **Execution blocked by SmartScreen**                     | Binary is unsigned or unrecognized by Windows             | Use a trusted distribution, allow execution explicitly, or contact the publisher |
| **Missing configuration**                                | Configuration files are missing or malformed              | Verify files in the `Config` folder and restore them from the repository sources |



---

## ➡️ Next Steps

* Explore container workflows
* Review domain services: [DockerBuildBoxSystem.Domain](https://github.com/ReflexLevel0/DockerBuildboxSystem/tree/main/src/DockerBuildBoxSystem.Domain)
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
