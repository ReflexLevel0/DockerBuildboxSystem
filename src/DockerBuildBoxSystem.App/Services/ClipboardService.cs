using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using DockerBuildBoxSystem.Contracts;

namespace DockerBuildBoxSystem.App.Services
{
    /// <summary>
    /// A WPF implementation of clipboard service, STA safe so it marshals the UI dispatcher.
    /// Put here instead of in DockerBuildBoxSystem.Domain since it have WPF dependencies (i.e this is a UI-specific implementation of the IClipboardInterface).
    /// </summary>
    public sealed class ClipboardService : IClipboardService
    {
        private readonly Dispatcher _dispatcher;

        public ClipboardService()
        {
            _dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
        }

        public async Task SetTextAsync(string text, CancellationToken ct = default)
        {
            text ??= string.Empty;

            if (_dispatcher.CheckAccess())
            {
                TrySetClipboard(text);
            }
            else
            {
                await _dispatcher.InvokeAsync(() => TrySetClipboard(text), DispatcherPriority.Send, ct);
            }
        }

        private static void TrySetClipboard(string text)
        {
            try
            {
                Clipboard.SetText(text);
            }
            catch (Exception)
            {
                //clipboard exceptions if failed... have some retry logic here?
            }
        }
    }
}
