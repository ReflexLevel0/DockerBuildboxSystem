using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DockerBuildBoxSystem.ViewModels.Common
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
            var result = new List<string>();
            if (string.IsNullOrWhiteSpace(cmd))
            {
                return result.ToArray();
            }

            var current = new StringBuilder();
            var inQuotes = false;

            foreach (var ch in cmd)
            {
                switch (ch)
                {
                    case '\"':
                        inQuotes = !inQuotes;
                        current.Append(ch);
                        break;
                    case ' ' when !inQuotes:
                        if (current.Length > 0)
                        {
                            result.Add(current.ToString());
                            current.Clear();
                        }

                        break;
                    default:
                        current.Append(ch);
                        break;
                }
            }

            if (current.Length > 0)
            {
                result.Add(current.ToString());
            }

            return result.ToArray();
        }
    }
}
