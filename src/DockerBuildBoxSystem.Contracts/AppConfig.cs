using Docker.DotNet.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DockerBuildBoxSystem.Contracts
{
    public class AppConfig
    {
        public required string BuildDirectoryPath { get; set; }
        public required HostConfig ContainerCreationParams { get; set; }
    }
}
