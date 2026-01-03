# DockerBuildboxSystem
## container_creation_args.json
File container_creation_args.json is used for specifying docker arguments which are used when a new container is being created. It is written in the JSON format and supports various arguments.

Example file that will assign 8GB of memory and 4 CPU cores to the container and automatically delete it when it stops running.
It also adds a read-only mount from host to container and a volume mount:
```json
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
```
[All parameters documentation](https://github.com/dotnet/Docker.DotNet/blob/master/src/Docker.DotNet/Models/HostConfig.Generated.cs)
