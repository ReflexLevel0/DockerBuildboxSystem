using System.Collections.Generic;

namespace DockerBuildBoxSystem.Contracts
{
    /// <summary>
    /// Interface based on region in standalone application for filesync.
    /// </summary>
    public interface IIgnorePatternMatcher
    {
        void LoadPatterns(string patterns);
        void AddPattern(string pattern);
        bool IsIgnored(string path);
        IEnumerable<string> GetPatterns();
    }
}
