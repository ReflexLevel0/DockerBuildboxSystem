using DockerBuildBoxSystem.Contracts;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DockerBuildBoxSystem.Domain
{
    /// <summary>
    /// Provides methods for validating command input values to ensure they do not contain potentially unsafe
    /// substrings.
    /// </summary>
    /// <remarks>This service is typically used to prevent command injection or other security vulnerabilities
    /// by checking for the presence of characters or patterns that are commonly used in shell commands. The validation
    /// logic is designed to be simple and fast, making it suitable for use in input sanitization scenarios.</remarks>
    public sealed class CommandValidatorService : ICommandValidatorService
    {

        private static readonly string[] UnsafeSubstrings =
        {
            "&&", // Command chaining
            "||", // Conditional chaining
            ";",  // Command separator
            "|",  // Piping
            "`",  // Command substitution
            "$(", // Subshell command substitution
            "<",  // Input redirection
            ">",  // Output redirection
            "\n", // Command splitting
            "\r"  // Command splitting
        };

        /// <summary>
        /// Checks if the provided value is safe by ensuring it does not contain any unsafe substrings.
        /// </summary>
        /// <param name="value">the value to check</param>
        /// <param name="errorMessage">the error message if the value is unsafe</param>
        /// <returns>true if the value is safe; otherwise, false</returns>
        public bool IsSafeValue(string value, out string? errorMessage)
        {
            errorMessage = string.Empty;

            if (string.IsNullOrWhiteSpace(value))
                return true;

            foreach (var token in UnsafeSubstrings)
            {
                if (value.Contains(token, StringComparison.Ordinal))
                {
                    errorMessage = $"Unsafe substring detected: '{token}'";
                    return false;
                }
            }
            return true;
        }
    }
}
