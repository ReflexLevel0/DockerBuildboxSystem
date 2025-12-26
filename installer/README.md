# Docker BuildBox System — Installer (Inno Setup)

This folder contains an **Inno Setup** installer script that produces a standard Windows wizard (`.exe`) installer.

## Requirements (for end users)
- Windows 10/11 x64
- Docker Desktop installed and running (required for container features)

## Requirements (to build the installer)
- .NET 8 SDK
- Inno Setup 6

## Before you publish: ensure config files are published
Your app loads these from disk at runtime:
- `Config/appsettings.json`
- `Config/container_creation_args.json`
- `Config/controls.json`
- `Assets/icons/*`

Make sure they are copied to the publish folder by adding `CopyToPublishDirectory` in `DockerBuildBoxSystem.App.csproj`
(see instructions in chat).

## Manual build
1) Publish:

```powershell
dotnet publish ..\src\DockerBuildBoxSystem.App -c Release -r win-x64 --self-contained true -o ..\publish\win-x64
```

2) Compile the installer:
- Open `DockerBuildBoxSystem.iss` in Inno Setup
- Click **Compile**

## Notes
- Installer defaults to **per-user install** (no admin required).
- Keep `AppId` constant for upgrades.

## Resulting installer flow (professional-grade)

Welcome
↓
License Agreement
↓
IMPORTANT USAGE TERMS (custom page)
   [✓ I understand containers may be stopped]
   [View Privacy Policy]
↓
Choose Install Location
↓
Install
↓
Finish
