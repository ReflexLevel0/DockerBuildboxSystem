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

        public async Task<List<UserCommand>>LoadAsync()
        {
            if (!File.Exists(_path)) return new List<UserCommand>();
            var json = await File.ReadAllTextAsync(_path);
            return JsonSerializer.Deserialize<List<UserCommand>>(json) ?? new List<UserCommand>();
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
