using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DockerBuildBoxSystem.Contracts
{
    /// <summary>
    /// Interface for managing user controls and variables.
    /// It provides methods to load, save, and manipulate user control definitions and their associated variables.
    /// </summary>
    public interface IUserControlService
    {
        Task <List<UserControlDefinition>> LoadUserControlsAsync();
        Task SaveUserControlAsync(List<UserControlDefinition> userControls);
        Task AddUserControlValueAsync(string id, string value);
        Task<string> RetrieveVariableAsync(string command, List<UserVariables>variables);
        List<UserVariables> LoadUserVariables(List<UserControlDefinition> controls);
    }
}
