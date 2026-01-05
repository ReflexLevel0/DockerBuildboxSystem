using DockerBuildBoxSystem.Contracts;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace DockerBuildBoxSystem.Domain
{
    /// <summary>
    /// Provides services for managing user controls and variables, including loading, saving, updating control definitions
    /// and retrieving variable values in command strings using a JSON file as persistent storage.
    /// </summary>
    /// <remarks> This service utilizes a JSON file located in the "config" directory of the application's base directory to store
    /// user control definitions and their associated variables.
    /// </remarks>
    public class UserControlService : IUserControlService
    {
        private readonly string _filePath;
        private readonly JsonSerializerOptions _jsonOptions;

        /// <summary>
        /// Initializes a new instance of the <see cref="UserControlService"/> class, setting up the file path for storing user control definitions
        /// and json serialization options for handling different control types.
        /// </summary>
        /// <remarks>This constructor ensures that the directory for the specified file path exists. If
        /// the directory does not exist, it will be created automatically.
        /// </remarks>
        /// <param name="filename">The name of the JSON file to use for storing user control definitions.</param>
        public UserControlService(string filename = "controls.json")
        {
            _filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Config", filename);

            // Ensure the Config directory exists
            Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);

            _jsonOptions = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                WriteIndented = true,
                // Register the custom converter for UserControlDefinition
                Converters = { new UserControlConverter() }
            };
        }

        /// <summary>
        /// Asynchronously loads user control definitions from the JSON file.
        /// </summary>
        /// <remarks>If the JSON file does not exist, an empty list is returned. The JSON file is expected to contain
        /// a serialized list of <see cref="UserControlDefinition"/>. If the file content cannot be deserialized, an empty list is returned.
        /// </remarks>
        /// <returns>A task representing the asynchronous operation, with a list of user control definitions as the result.</returns>
        public async Task<List<UserControlDefinition>> LoadUserControlsAsync()
        {
            if (!File.Exists(_filePath))
                return new List<UserControlDefinition>();

            var json = await File.ReadAllTextAsync(_filePath);

            return JsonSerializer.Deserialize<List<UserControlDefinition>>(json, _jsonOptions)
                ?? new List<UserControlDefinition>();
        }
        /// <summary>
        /// Saves the provided list of user control definitions to the JSON file asynchronously.
        /// </summary>
        /// <remarks>This method serializes the list of user control definitions into JSON format
        /// and writes it to the specified file.
        /// </remarks>
        /// <param name="userControls">A list of user control definitions to save.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public async Task SaveUserControlAsync(List<UserControlDefinition> userControls)
        {
            var json = JsonSerializer.Serialize(userControls, _jsonOptions);
            await File.WriteAllTextAsync(_filePath, json);
        }

        /// <summary>
        /// Adds or updates the value of a user control identified by the given ID asynchronously. 
        /// </summary>
        /// <param name="id">The ID of the user control to update.</param>
        /// <param name="value">The new value to set for the user control.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public async Task AddUserControlValueAsync(string id, string value)
        {
            // Load existing controls
            var controlList = await LoadUserControlsAsync();
            // Find the control by ID
            var control = controlList.FirstOrDefault(c =>
            {
                return c switch
                {
                    TextBoxCommand t => t.Id == id,
                    DropdownOption d => d.Id == id,
                    _ => false
                };
            });

            if (control != null)
            {
                // Update the value based on control type
                switch (control)
                {
                    case TextBoxCommand t:
                        t.Value = value;
                        break;
                    case DropdownOption d:
                        if (d.Values.Contains(value))
                        {
                            d.Value = value;
                        }
                        break;
                }
                await SaveUserControlAsync(controlList);
            }

        }

        /// <summary>
        /// Loads user variables from the provided list of user control definitions.
        /// </summary>
        /// <param name="controls">The list of user control definitions.</param>
        /// <returns>A list of user variables extracted from the controls.</returns>
        public List<UserVariables> LoadUserVariables(List<UserControlDefinition> controls)
        {
            var userVariables = new List<UserVariables>();
            foreach (var control in controls)
            {
                switch (control)
                {
                    // Extract variables based on control type
                    case TextBoxCommand t when !string.IsNullOrEmpty(t.Id):
                        userVariables.Add(new UserVariables(t.Id!, t.Value ?? ""));
                        break;
                    case DropdownOption d when !string.IsNullOrEmpty(d.Id):
                        userVariables.Add(new UserVariables(d.Id, d.Value ?? ""));
                        break;
                }
            }
            return userVariables;
        }

        /// <summary>
        /// Replaces variable tokens in the command string with their corresponding values from the user variables list asynchronously.
        /// </summary>
        /// <remarks>Placeholders in the command string must match the format <c>${variableName}</c>,
        /// where <c>variableName</c> corresponds to an Id in the user-defined variables.</remarks>
        /// <param name="command">The command string containing variable tokens.</param>
        /// <param name="userVariables">The list of user variables to replace in the command.</param>
        /// <returns>The command string with variables replaced by their values.</returns>
        public async Task<string> RetrieveVariableAsync(string command, List<UserVariables> userVariables)
        {
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
