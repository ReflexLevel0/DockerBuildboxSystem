using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DockerBuildBoxSystem.Contracts;
using DockerBuildBoxSystem.Domain;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DockerBuildBoxSystem.ViewModels.ViewModels
{
    /// <summary>
    /// Represents a view model that manages environment variables and provides commands for loading and editing them in
    /// the user interface.
    /// </summary>
    /// <remarks>This view model exposes an observable collection of environment variables and provides
    /// commands to load variables from the environment and open the environment file for editing. It is intended for
    /// use in MVVM scenarios where environment variable management is required in the UI. Changes to environment
    /// variables are automatically saved when their key or value is modified.</remarks>
    public partial class EnvironmentViewModel: ObservableObject
    {
        private readonly IEnvironmentService _envService;

        public ObservableCollection<EnvVariable> EnvVariables { get; }
        public EnvironmentViewModel(IExternalProcessService externalProcessService)
        {
            _envService = new EnvironmentService(externalProcessService);
            EnvVariables = new ObservableCollection<EnvVariable>();

        }

        /// <summary>
        /// Loads environment variables asynchronously and populates the EnvVariables collection.
        /// </summary>
        /// <returns>a task representing the asynchronous operation</returns>
        /// <remarks>This method clears the existing environment variables and loads them from the .env file.
        /// It subscribes to property change events on each EnvVariable to automatically save changes.</remarks>
        [RelayCommand]
        public async Task LoadEnvASync()
        {
            EnvVariables.Clear();
            var envs = await _envService.LoadEnvAsync();
            foreach (var env in envs)
            {
                env.PropertyChanged += EnvVariableChanged;
                EnvVariables.Add(env);
            }
        }

        /// <summary>
        /// Opens the environment file for editing.
        /// </summary>
        [RelayCommand]
        public void OpenEnvFile()
        {
            _envService.OpenEnvFile();
        }

        /// <summary>
        /// This method is called when an environment variable's property changes.
        /// It updates the environment file with the new values.
        /// </summary>
        /// <param name="sender"> the environment variable that changed</param>
        /// <param name="e"> the property change event arguments</param>
        private async void EnvVariableChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(EnvVariable.Value) || e.PropertyName == nameof(EnvVariable.Key))
            {
                await _envService.SaveEnvAsync(EnvVariables.ToList());
            }
        }
    }
}
