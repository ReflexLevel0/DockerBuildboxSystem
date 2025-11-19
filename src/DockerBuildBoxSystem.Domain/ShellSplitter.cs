using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DockerBuildBoxSystem.Domain
{
    public static class ShellSplitter
    {
        /// <summary>
        /// Splits a shell-like command string into argv tokens (very simple splitting by spaces
        /// </summary>
        /// <param name="cmd">The command string.</param>
        /// <returns>The argv array.</returns>
        public static string[] SplitShellLike(string cmd)
        {
            //simple split
            return cmd.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        }
    }
}
