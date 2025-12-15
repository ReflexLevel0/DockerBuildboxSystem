using DockerBuildBoxSystem.Contracts;

namespace DockerBuildBoxSystem.ViewModels.Common
{
    /// <summary>
    /// A logger that writes messages to a UILineBuffer.
    /// </summary>
    public class ViewModelLogger : IViewModelLogger
    {
        private readonly UILineBuffer _uiHandler;

        public ViewModelLogger(UILineBuffer uiHandler)
        {
            _uiHandler = uiHandler;
        }

        /// <inheritdoc />
        public void LogWithNewline(string message, bool isError = false, bool isImportant = false)
        {
            _uiHandler.EnqueueLine(message + "\r\n", isError, isImportant);
        }

        /// <inheritdoc />
        public void Log(string message, bool isError = false, bool isImportant = false)
        {
            _uiHandler.EnqueueLine(message, isError, isImportant);
        }
    }
}
