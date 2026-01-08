# 🐳 DockerBuildBoxSystem

[![.NET](https://img.shields.io/badge/.NET-8.0-blue)](https://dotnet.microsoft.com/)
[![Windows](https://img.shields.io/badge/OS-Windows-lightgrey)](https://www.microsoft.com/windows)
[![CPU](https://img.shields.io/badge/CPU-x64-lightgrey)]()

DockerBuildBoxSystem is a Windows desktop application (WPF, .NET) that helps you build images, create and manage containers and volumes, run commands inside containers (single-run and interactive), and synchronize files between your host and container. It wraps Docker operations with a friendly UI and adds automation for repeatable build workflows.


**Contents**
- [📖 Introduction](#-introduction)
- [🏗️ Architecture Overview](#%EF%B8%8F-architecture-overview)
- [💻 Installation](#-installation)
- [⚙️ Configuration](#%EF%B8%8F-configuration)
- [🎛️ User Controls](#%EF%B8%8F-user-controls)
- [📝 Commands & Arguments](#%EF%B8%8F-user-controls)
- [🚀 Quick Start](#-quick-start)
- [🧪 Testing](#-testing)
- [⚠️ Troubleshooting](#%EF%B8%8F-troubleshooting)

## 📖 Introduction
DockerBuildBoxSystem centralizes common Docker developer workflows:
- Build images and manage them locally
- Create, start, stop, and remove containers
- Manage volumes and mounts
- Execute commands in containers (single-run or interactive shell)
- Synchronize host files into containers reliably
- View logs and track tasks via the UI



<img
  src="https://github.com/ReflexLevel0/DockerBuildboxSystem/blob/DBS-208-add-readme/docs/product/application.png"
  width="70%"
  alt="Docker Buildbox App User Interface"
/>

## 🏗️ Architecture Overview
The app is organized into layered projects with clear contracts and domain services:

* **Presentation**: WPF UI with view models and JSON-defined controls; handles binding, commands, input, and progress/log display.
* **Core**: Domain services implementing workflows for images, containers, volumes, commands, file transfer, and continuous sync; defines contracts, models, and business rules independent of UI/OS.
* **Infrastructure**: Adapters and concrete implementations integrating with Docker and the OS (Docker.DotNet, external processes, filesystem, environment, settings persistence, logging).


<img
  src="https://github.com/ReflexLevel0/DockerBuildboxSystem/blob/development/docs/plantumlimages/Architecture.png"
  width="50%"
  alt="Architecture Overview Diagram"
/>

Explore the diagrams in the docs to understand flows and tiers in detail.



## 💻 Installation
Prerequisites:
- Windows 10/11
- Docker Desktop (running)
- .NET 8 SDK (for building from source)
- Visual Studio 2022 (optional, for IDE build)

Options:
- Installer: See [Installer](https://github.com/ReflexLevel0/DockerBuildboxSystem/blob/development/installer/README.md) and [Inno Setup script](https://github.com/ReflexLevel0/DockerBuildboxSystem/blob/development/installer/DockerBuildBoxSystem.iss).
- From source:
	- Build the [Solution](DockerBuildBoxSystem.sln) in Visual Studio or via CLI:
		```bash
		dotnet restore
		dotnet build DockerBuildBoxSystem.sln -c Release
		```
	- Run the WPF app from src/DockerBuildBoxSystem.App.

## ⚙️ Configuration
The app reads settings and UI definitions from JSON files:
- [App settings](https://github.com/ReflexLevel0/DockerBuildboxSystem/blob/development/src/DockerBuildBoxSystem.App/Config/appsettings.json) - app settings, with optional [Development overrides](https://github.com/ReflexLevel0/DockerBuildboxSystem/blob/development/src/DockerBuildBoxSystem.App/Config/appsettings.Development.json).
- [UI controls](https://github.com/ReflexLevel0/DockerBuildboxSystem/blob/development/src/DockerBuildBoxSystem.App/Config/controls.json) - defines inputs and buttons shown in the UI.
- [Container creation arguments & build directory location](https://github.com/ReflexLevel0/DockerBuildboxSystem/blob/development/src/DockerBuildBoxSystem.App/Config/config.json) - defines parameters for container creation and the location of the build directory.


Sample `config.json` demonstrating how to assign 8 GB memory and 4 CPUs, enable auto-removal on stop, and configure both a read-only bind mount and a volume mount.
```json
{
	"BuildDirectoryPath": "build",
	"ContainerCreationParams": 
	{
		"AutoRemove": true,
		"Memory": 8000000000,
		"CpusetCpus": 4,
		"Mounts": [
			{
				"Type": "bind",
				"Source": "C:/Users/User/videos",
				"Target": "/server/videos",
				"ReadOnly": true,
				"BindOptions": { "CreateMountpoint": true }
			},
			{
				"Type": "volume",
				"Source": "gameVolume",
				"Target": "/games"
			}
		]
	}
}
```
Reference: HostConfig options in Docker.DotNet (see the project's [documentation](https://github.com/dotnet/Docker.DotNet/blob/master/src/Docker.DotNet/Models/HostConfig.Generated.cs)).


## 🎛️ User Controls
Controls are defined in [`controls.json`](https://github.com/ReflexLevel0/DockerBuildboxSystem/blob/development/src/DockerBuildBoxSystem.App/Config/controls.json) and are rendered dynamically. The current set includes:

#### Text Inputs

| Control ID        | Description                      | Container Path |
|-------------------|----------------------------------|----------------|
| `gcc_input_file`  | Input C file path                | `/data`        |
| `gcc_output_file` | Output binary name               | `/data/build`  |

#### Dropdowns

| Control ID | Description            | Options                              |
|------------|------------------------|--------------------------------------|
| `make_arg` | Make target to execute | `all`, `hello.exe`, `hello.o`, `clean`|

#### Actions

| Control ID    | Icon        | Command Executed in Container |
|---------------|-------------|--------------------------------|
| `gcc`         | `gcc`       | `mkdir -p /data/build && gcc -o /data/build/${gcc_output_file} /data/${gcc_input_file}` |
| `make`        | `linux`     | `mkdir -p /data/build && (cd /data && make ${make_arg})` |
| `list`        | `list`      | `ls ${dir}` |
| `show-env`    | `show-env`  | `printenv` |
| `check-disk`  | `check-disk`| `df -h` |
| `uptime`      | `uptime`    | `uptime` |
| `processes`   | `processes` | `ps aux` |

You can add or modify controls by editing `controls.json` to fit your workflow.

## 📝 Commands & Arguments
Internally, the app issues standard Docker commands and passes creation arguments from `container_creation_args.json`. Common mappings:
- `AutoRemove: true` → `docker run --rm ...`
- `Memory: 8000000000` → `docker run --memory 8000000000 ...`
- `CpusetCpus: 4` → `docker run --cpuset-cpus 4 ...`
- Bind mount: `Type: bind` → `docker run -v C:/Users/User/videos:/server/videos:ro ...`
- Volume mount: `Type: volume` → `docker run -v gameVolume:/games ...`

## 🚀 Quick Start
1) Build a sample image (optional): use the mock build box in [Buildbox Mock](https://github.com/ReflexLevel0/DockerBuildboxSystem/blob/development/docs/buildbox_mock).
```bash
docker build -t buildbox-mock -f docs/buildbox_mock/Dockerfile docs/buildbox_mock
```

2) Start DockerBuildBoxSystem:
- Launch the app from src/DockerBuildBoxSystem.App after building.

3) Create a container:
- Provide `config.json` as needed (memory, CPUs, mounts, volumes).
- Create and start the container from the UI.

4) Run commands:
- Fill `gcc_input_file` and `gcc_output_file`, then press the `gcc` button to compile.
- Use `make_arg` and press the `make` button to build via `make`.
- Explore environment and system with the other buttons (`list`, `show-env`, `check-disk`, `uptime`, `processes`).

5) Sync files:
- Configure file sync to mirror host files into your container paths for rapid iteration.

## 🧪 Testing
Run the test suites:
```bash
dotnet test DockerBuildBoxSystem.sln
```

## ⚠️ Troubleshooting
- Ensure Docker Desktop is running and accessible.
- If commands fail inside the container, verify mounts and paths (e.g., `/data`).
- Adjust `config.json` for resource limits and mounts.
- For UI controls using `${dir}`, set the directory value appropriately.
