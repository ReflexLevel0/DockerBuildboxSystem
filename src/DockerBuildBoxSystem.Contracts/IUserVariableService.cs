using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DockerBuildBoxSystem.Contracts
{
    /// <summary>
    /// Defines methods for managing user-defined variables, including adding, retrieving, loading, and saving variable
    /// data asynchronously.
    /// </summary>
    /// <remarks>Implementations of this interface are expected to provide persistent or in-memory storage for
    /// user variables. All operations are asynchronous and may involve I/O or database access, depending on the
    /// implementation.</remarks>
    public interface IUserVariableService
    {
        Task AddUserVariableAsync(string id, string value);
        Task<List<UserVariables>> LoadUserVariablesAsync();
        Task SaveUserVariablesAsync(List<UserVariables> userVariables);
        Task<string> RetrieveVariableAsync(string command);
    }
}
