using CommunityToolkit.Mvvm.ComponentModel;



namespace DockerBuildBoxSystem.Contracts
{
    /// <summary>
    /// Represents an environment variable with a key and a value.
    /// </summary>
    /// <remarks>
    /// This class is used to store and manage environment variables for Docker containers.
    /// It is observable, allowing for data binding in MVVM architectures.
    /// </remarks>
    public partial class EnvVariable : ObservableObject
    {
        [ObservableProperty]
        private string? key;
        [ObservableProperty]
        private string? value;

    }
}
