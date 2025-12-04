using DockerBuildBoxSystem.Contracts;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace DockerBuildBoxSystem.Domain
{
    public class IgnorePatternMatcher : IIgnorePatternMatcher
    {
        private readonly List<Regex> _ignorePatterns = new();

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

        public bool IsIgnored(string path)
        {
            string normalized = path.Replace('\\', '/');
            foreach (var regex in _ignorePatterns)
                if (regex.IsMatch(normalized))
                    return true;
            return false;
        }

        //The GetIgnoreSummary method, just renamed.
        public IEnumerable<string> GetPatterns()
        {
            foreach (var r in _ignorePatterns)
                yield return r.ToString();
        }
    }
}
