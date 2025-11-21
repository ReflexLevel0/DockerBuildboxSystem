using System.Threading.Tasks;

namespace DockerBuildBoxSystem.Contracts
{
    /// <summary>
    /// Abstraction for clipboard interactions.
    /// USed so ViewModels can copy text without having to do any UI dependencies.
    /// </summary>
    public interface IClipboardService
    {
        /// <summary>
        /// Sets plain text content to the clipboard.
        /// Need to ensure it is executed on an STA thread.
        /// </summary>
        Task SetTextAsync(string text, CancellationToken ct = default);
    }
}
