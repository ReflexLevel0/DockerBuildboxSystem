using DockerBuildBoxSystem.Contracts;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace DockerBuildBoxSystem.Domain
{
    public class IgnorePatternMatcher : IIgnorePatternMatcher
    {
        private readonly List<Regex> _ignorePatterns = new();

        /// <summary>
        /// Loads ignore patterns from a string and replaces any existing patterns.
        /// </summary>
        /// <remarks>Existing ignore patterns are cleared before loading new ones. Empty or
        /// whitespace-only input results in all patterns being cleared.</remarks>
        /// <param name="patterns">A string containing one or more ignore patterns, separated by line breaks. Each line represents a single
        /// pattern.</param>
        public void LoadPatterns(string patterns)
        {
            _ignorePatterns.Clear();
            if (string.IsNullOrWhiteSpace(patterns)) return;

            string[] lines = patterns.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var line in lines)
            {
                AddPattern(line);
            }
        }

        /// <summary>
        /// Adds a file or directory pattern to the ignore list.
        /// </summary>
        /// <param name="pattern">The pattern to add. Supports wildcards: <c>*</c> matches any sequence of characters except directory
        /// separators, <c>**</c> matches any sequence of characters including directory separators, and <c>?</c>
        /// matches any single character. Leading <c>./</c> is ignored. Trailing slashes indicate directory patterns.</param>
        public void AddPattern(string pattern)
        {
            pattern = pattern.Trim();
            if (pattern.Length == 0) return;

            if (pattern.StartsWith("./"))
                pattern = pattern[2..];

            string regexPattern = Regex.Escape(pattern)
                .Replace(@"\*\*", ".*")
                .Replace(@"\*", "[^/\\\\]*")
                .Replace(@"\?", ".");

            if (pattern.EndsWith("/"))
                regexPattern = $"{regexPattern}.*";

            regexPattern = ".*" + regexPattern + ".*";
            _ignorePatterns.Add(new Regex(regexPattern, RegexOptions.IgnoreCase));
        }

        /// <summary>
        /// Determines whether the specified path matches any of the configured ignore patterns.
        /// </summary>
        /// <param name="path">The file or directory path to evaluate. May use either forward or backward slashes as separators.</param>
        /// <returns><see langword="true"/> if the path matches an ignore pattern and should be excluded; otherwise, <see
        /// langword="false"/>.</returns>
        public bool IsIgnored(string path)
        {
            string normalized = path.Replace('\\', '/');
            foreach (var regex in _ignorePatterns)
                if (regex.IsMatch(normalized))
                    return true;
            return false;
        }

        /// <summary>
        /// Returns an enumerable collection of all ignore patterns as strings.
        /// </summary>
        /// <returns>An <see cref="IEnumerable{String}"/> containing the string representations of all ignore patterns. The
        /// collection will be empty if no ignore patterns are defined.</returns>
        public IEnumerable<string> GetPatterns()
        {
            foreach (var r in _ignorePatterns)
                yield return r.ToString();
        }
    }
}
