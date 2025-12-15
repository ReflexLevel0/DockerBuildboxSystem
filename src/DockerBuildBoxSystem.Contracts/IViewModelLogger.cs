namespace DockerBuildBoxSystem.Contracts
{
    public interface IViewModelLogger
    {
        /// <summary>
        /// Logs a message with newline appended.
        /// </summary>
        /// <param name="message">The message to log.</param>
        /// <param name="isError">Indicates if the message is an error.</param>
        /// <param name="isImportant">Indicates if the message is important.</param>
        void LogWithNewline(string message, bool isError = false, bool isImportant = false);

        /// <summary>
        /// Logs a message.
        /// </summary>
        /// <param name="message">The message to log.</param>
        /// <param name="isError">Indicates if the message is an error.</param>
        /// <param name="isImportant">Indicates if the message is important.</param>
        void Log(string message, bool isError = false, bool isImportant = false);
    }
}
