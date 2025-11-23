using DockerBuildBoxSystem.Contracts;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace DockerBuildBoxSystem.Domain
{
    /// <summary>
    /// Provides functionality for managing user-defined variables, including loading, saving, and adding variables.
    /// </summary>
    /// <remarks>This service reads and writes user variables to a JSON file stored in the application's base
    /// directory. The file name can be customized via the constructor. The service ensures that the directory for the
    /// file exists before performing any operations. Variables are serialized and deserialized using camelCase naming
    /// conventions.</remarks>
    public class UserVariableService
    {
        private readonly string _filePath;
        private readonly JsonSerializerOptions _jsonOptions;

        /// <summary>
        /// Initializes a new instance of the <see cref="UserVariableService"/> class, setting up the file path and JSON
        /// serialization options for managing user variables.
        /// </summary>
        /// <remarks>This constructor ensures that the directory for the specified file path exists. If
        /// the directory does not exist, it will be created automatically. JSON serialization options are configured to
        /// use camelCase naming and indented formatting.</remarks>
        /// <param name="fileName">The name of the JSON file used to store user variables. Defaults to "user_variables.json". The file will be
        /// created in the application's base directory if it does not already exist.</param>
        public UserVariableService(string fileName = "user_variables.json")
        {
            // Set the file path to the specified file name in the application's base directory
            _filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, fileName);
            _jsonOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true
            };
            // Ensure the Config directory exists
            var dir = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            
        }

        /// <summary>
        /// Asynchronously loads user-defined variables from a JSON file.
        /// </summary>
        /// <remarks>If the file specified by the internal file path does not exist, an empty list is
        /// returned. The JSON file is expected to contain a serialized list of <see cref="UserVariables"/> objects. If
        /// the file content cannot be deserialized, an empty list is returned.</remarks>
        /// <returns>A task that represents the asynchronous operation. The task result contains a list of  <see
        /// cref="UserVariables"/> objects loaded from the file, or an empty list if the file does not exist  or the
        /// content cannot be deserialized.</returns>
        public async Task<List<UserVariables>> LoadUserVariablesAsync()
        {
            if (!File.Exists(_filePath))
            {
                return new List<UserVariables>();
            }
            var json = await File.ReadAllTextAsync(_filePath);
            return JsonSerializer.Deserialize<List<UserVariables>>(json, _jsonOptions)
                ?? new List<UserVariables>();
        }

        /// <summary>
        /// Saves the specified list of user variables to a file asynchronously.
        /// </summary>
        /// <remarks>The user variables are serialized to JSON format and written to the file specified by
        /// the internal file path.</remarks>
        /// <param name="userVariables">A list of <see cref="UserVariables"/> objects to be saved. Cannot be null.</param>
        /// <returns>A task that represents the asynchronous save operation.</returns>
        public async Task SaveUserVariablesAsync(List<UserVariables> userVariables)
        {
            var json = JsonSerializer.Serialize(userVariables, _jsonOptions);
            await File.WriteAllTextAsync(_filePath, json);
        }

        /// <summary>
        /// Adds or updates a user-defined variable asynchronously.
        /// </summary>
        /// <remarks>If a variable with the specified key already exists, its value is updated. Otherwise,
        /// a new variable is added.</remarks>
        /// <param name="key">The unique key identifying the user variable. Cannot be <see langword="null"/> or empty.</param>
        /// <param name="value">The value to associate with the specified key. Cannot be <see langword="null"/>.</param>
        /// <returns>A task that represents the asynchronous operation.</returns>
        public async Task AddUserVariableAsync(string key, string value)
        {
            // Load existing variables
            var userVariables = await LoadUserVariablesAsync();
            // Check if the key already exists
            var existingVariable = userVariables.FirstOrDefault(uv => uv.Key == key);
            if (existingVariable != null)
            {
                existingVariable.Value = value;
            }
            else
            {
                userVariables.Add(new UserVariables(key, value));
            }
            await SaveUserVariablesAsync(userVariables);
        }

        /// <summary>
        /// Removes a user-defined variable identified by the specified key.
        /// </summary>
        /// <remarks>This method loads the current user variables, searches for a variable with the
        /// specified key, and removes it if found. The updated list of user variables is then saved. If no variable
        /// with the specified key exists, the method returns <see langword="false"/>.</remarks>
        /// <param name="key">The key of the user variable to remove. Cannot be <see langword="null"/> or empty.</param>
        /// <returns><see langword="true"/> if the variable was successfully removed; otherwise, <see langword="false"/> if no
        /// variable with the specified key exists.</returns>
        public async Task<bool> RemoveUserVariableAsync(string key)
        {
            var userVariables = await LoadUserVariablesAsync();
            var variableToRemove = userVariables.FirstOrDefault(uv => uv.Key == key);
            if (variableToRemove != null)
            {
                userVariables.Remove(variableToRemove);
                await SaveUserVariablesAsync(userVariables);
                return true;
            }
            return false;
        }

        /// <summary>
        /// Clears all user-defined variables asynchronously.
        /// </summary>
        /// <remarks>This method removes all existing user-defined variables by replacing them with an
        /// empty collection. It performs the operation asynchronously and ensures that the changes are saved.</remarks>
        /// <returns>A task that represents the asynchronous operation.</returns>
        public async Task ClearAllUserVariablesAsync()
        {
            await SaveUserVariablesAsync(new List<UserVariables>());
        }


        /// <summary>
        /// Replaces placeholders in the specified command with their corresponding user-defined variable values.
        /// </summary>
        /// <remarks>Placeholders in the command string must match the format <c>${variableName}</c>,
        /// where <c>variableName</c> corresponds to a key in the user-defined variables.</remarks>
        /// <param name="command">The command string containing placeholders in the format <c>${variableName}</c> to be replaced.</param>
        /// <returns>A <see cref="string"/> with all recognized placeholders replaced by their respective values. If no
        /// placeholders are found, the original command is returned.</returns>
        public async Task<string> RetrieveVariableAsync(string command)
        {
            var userVariables = await LoadUserVariablesAsync();
            foreach (var variable in userVariables)
            {
                // Create the token format ${key}
                string token = $"${{{variable.Key}}}";
                if (command.Contains(token))
                {
                    command = command.Replace($"${{{variable.Key}}}", variable.Value);
                }
            }
            return command;
        }
    }
}
