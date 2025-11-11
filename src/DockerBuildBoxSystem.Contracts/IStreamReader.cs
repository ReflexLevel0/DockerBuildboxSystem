using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace DockerBuildBoxSystem.Contracts
{
    public interface IStreamReader : IAsyncDisposable
    {
        bool IsRunning { get; }
        IAsyncEnumerable<(bool IsStdErr, string Line)> RunAsync(IContainerService svc, string containerId, string[]? args = null, CancellationToken ct = default);
        Task StopAsync();
    }
}
