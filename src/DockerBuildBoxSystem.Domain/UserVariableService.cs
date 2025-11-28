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
    /// Provides services for managing user-defined variables, including loading, saving, adding, and substituting
    /// variables in command strings using a JSON file as persistent storage.
    /// </summary>
    /// <remarks>This service enables asynchronous operations for handling user variables, which are stored in
    /// a JSON file located in the application's base directory. Placeholders in command strings can be replaced with
    /// user-defined variable values using the supported format. The service ensures that the storage file and its
    /// directory are created if they do not exist.</remarks>
    public class UserVariableService: IUserVariableService
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
        /// Adds a user variable with the specified identifier and value asynchronously. If a variable with the given
        /// identifier already exists, its value is updated.
        /// </summary>
        /// <param name="id">The unique identifier for the user variable to add or update. Cannot be null.</param>
        /// <param name="value">The value to assign to the user variable. Cannot be null.</param>
        /// <returns>A task that represents the asynchronous add or update operation.</returns>
        public async Task AddUserVariableAsync(string id, string value)
        {
            // Load existing variables
            var userVariables = await LoadUserVariablesAsync();
            // Check if the key already exists
            var existingVariable = userVariables.FirstOrDefault(uv => uv.Id == id);
            if (existingVariable != null)
            {
                // if the value is the same, no need to update
                if (existingVariable.Value == value)
                    return;
                
                existingVariable.Value = value;
            }
            
            else
            {
                userVariables.Add(new UserVariables(id, value));
            }
            await SaveUserVariablesAsync(userVariables);
        }


        /// <summary>
        /// Replaces placeholders in the specified command with their corresponding user-defined variable values.
        /// </summary>
        /// <remarks>Placeholders in the command string must match the format <c>${variableName}</c>,
        /// where <c>variableName</c> corresponds to an Id in the user-defined variables.</remarks>
        /// <param name="command">The command string containing placeholders in the format <c>${variableName}</c> to be replaced.</param>
        /// <returns>A <see cref="string"/> with all recognized placeholders replaced by their respective values. If no
        /// placeholders are found, the original command is returned.</returns>
        public async Task<string> RetrieveVariableAsync(string command)
        {
            var userVariables = await LoadUserVariablesAsync();
            foreach (var variable in userVariables)
            {
                // Create the token format ${variableName}
                string token = $"${{{variable.Id}}}";
                if (command.Contains(token))
                {
                    command = command.Replace($"${{{variable.Id}}}", variable.Value);
                }
            }
            return command;
        }
    }
}
