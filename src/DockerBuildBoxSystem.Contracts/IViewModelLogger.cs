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

        /// <summary>
        /// Discards all log entries that have been queued but not yet written.
        /// </summary>
        /// <remarks>Use this method to clear any pending log messages that have not been persisted. This
        /// can be useful in scenarios where queued logs are no longer relevant or should not be saved, such as during
        /// application shutdown or after a critical error.</remarks>
        void DiscardPendingLogs();
    }
}
