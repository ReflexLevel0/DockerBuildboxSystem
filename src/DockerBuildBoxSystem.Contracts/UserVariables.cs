using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DockerBuildBoxSystem.Contracts
{
    /// <summary>
    /// Represents a key-value pair used to store user-defined variables.
    /// </summary>
    /// <remarks>This class is designed to hold a pair of strings, where the <see cref="Key"/> represents the
    /// name  or identifier of the variable, and the <see cref="Value"/> represents its associated value.  It can be
    /// used in scenarios where dynamic or user-defined variables need to be managed.</remarks>
    public class UserVariables
    {
        public string Key { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;

        public UserVariables()
        {
        }

        public UserVariables(string key, string value)
        {
            Key = key;
            Value = value;
        }
    }
}
