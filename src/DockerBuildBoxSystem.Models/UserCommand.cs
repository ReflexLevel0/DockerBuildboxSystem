namespace DockerBuildBoxSystem.Models
{
    /// <summary>
    /// Represents a user-defined command with an associated label and a sequence of command arguments.
    /// </summary>
    /// <remarks>This class is designed to encapsulate a command that can be executed, along with a
    /// descriptive label. The <see cref="Command"/> property contains the sequence of arguments that define the
    /// command.</remarks>
    public sealed class UserCommand
    {
        public string Label { get; set; } = string.Empty;
        public string[] Command { get; set; } = Array.Empty<string>();

    }
}
