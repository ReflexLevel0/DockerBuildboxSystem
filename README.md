# DockerBuildboxSystem
## container_creation_args.json
File container_creation_args.json is used for specifying docker arguments which are used when a new container is being created. It is written in the JSON format and supports various arguments.

Example file that will assign 8GB of memory and 4 CPU cores to the container and automatically delete it when it stops running. It also adds two mounts, first which is read-write and second is read-only:
```json
{
	"AutoRemove": true,
	"Memory": 8000000000,
	"CpusetCpus": 4,
	"Mounts": [
		{
			"Type": "bind",
			"Source": "C:/Users/User/images",
			"Target": "/images"
		},
		{
			"Type": "bind",
			"Source": "C:/Users/User/videos",
			"Target": "/videos",
			"ReadOnly": true
		},
	]
}
```
[All parameters documentation](https://github.com/dotnet/Docker.DotNet/blob/master/src/Docker.DotNet/Models/HostConfig.Generated.cs)
