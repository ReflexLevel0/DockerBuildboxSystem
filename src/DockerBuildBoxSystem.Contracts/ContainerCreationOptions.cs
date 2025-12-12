using System.Collections.Generic;
using Docker.DotNet;
using Docker.DotNet.Models;

namespace DockerBuildBoxSystem.Contracts
{
    public class ContainerCreationOptions
    {
        public required string ImageName { get; init; }
        public string? ContainerName { get; init; }
        public string? ContainerRootPath { get; init; } = "/data";
        public required HostConfig Config { get; init; }
    }
}
