using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using DockerBuildBoxSystem.Models;

namespace DockerBuildBoxSystem.Domain
{
    public class UserCommandService
    {
        private readonly string _path = string.Empty;
        private const int MaxCommands = 7;

        public UserCommandService(string? path = null)
        {
            _path = path ?? Path.Combine(AppContext.BaseDirectory, "Config", "commands.json");
        }


        public async Task SaveAsync(List<UserCommand> commands)
        {
            if (commands.Count > MaxCommands)
            {
                commands = commands.Take(MaxCommands).ToList();
            }
            var json = JsonSerializer.Serialize(commands, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(_path, json);
        }

        /// <summary>
        /// Asynchronously loads user-defined commands from a JSON configuration file.
        /// </summary>
        /// <remarks>If the specified configuration file does not exist, a default set of commands is
        /// created, serialized to the file, and then loaded. The commands are deserialized into a list of <see
        /// cref="UserCommand"/> objects and added to the <c>UserCommands</c> collection.</remarks>
        /// <param name="filename">The name of the configuration file to load. Defaults to "commands.json".</param>
        /// <returns>A task that represents the asynchronous operation.</returns>
        public async Task<List<UserCommand>> LoadAsync(string filename = "commands.json")
        {
            // Determine the path to the configuration file
            var configPath = Path.Combine(AppContext.BaseDirectory, "Config", filename);
            // If the file does not exist, create it with default commands
            if (!File.Exists(configPath))
            {
                var defaultCmds = new[]
                {
                    new UserCommand { Label = "List /", Command = ["ls", "/"] },
                    new UserCommand { Label = "Check Disk", Command = ["df", "-h"] },
                };
                Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
                await File.WriteAllTextAsync(configPath,
                    JsonSerializer.Serialize(defaultCmds, new JsonSerializerOptions { WriteIndented = true }));
            }
            // Read and deserialize the commands from the configuration file
            var json = await File.ReadAllTextAsync(configPath);
            return JsonSerializer.Deserialize<List<UserCommand>>(json) ?? new List<UserCommand>();
        }

        public async Task AddAsync(UserCommand command)
        {
            var cmds = await LoadAsync();
            if (cmds.Count >= MaxCommands)
            {
                throw new InvalidOperationException($"Cannot add more than {MaxCommands} commands.");
            }
            cmds.Add(command);
            await SaveAsync(cmds);
        }


        public async Task RemoveAtAsync(int index)
        {
            var cmds = await LoadAsync();
            if (index < 0 || index >= cmds.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(index), "Index is out of range.");
            }
            cmds.RemoveAt(index);
            await SaveAsync(cmds);
        }
    }
}
