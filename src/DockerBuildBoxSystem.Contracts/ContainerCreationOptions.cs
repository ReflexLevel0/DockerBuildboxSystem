using System.Collections.Generic;

namespace DockerBuildBoxSystem.Contracts
{
    public class ContainerCreationOptions
    {
        public required string ImageName { get; init; }
        public string? ContainerName { get; init; }
        public IEnumerable<(string Source, string Target, string? Options)>? VolumeBindings { get; init; }
        public long? Memory { get; init; }
        public long? MemorySwap { get; init; }
        public long? CpuShares { get; init; }
        public long? NanoCpus { get; init; }
    }
}
