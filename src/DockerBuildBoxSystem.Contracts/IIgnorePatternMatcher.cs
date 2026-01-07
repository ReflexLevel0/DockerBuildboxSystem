using System.Collections.Generic;

namespace DockerBuildBoxSystem.Contracts
{
    public interface IIgnorePatternMatcher
    {
        void LoadPatterns(string patterns);
        void AddPattern(string pattern);
        bool IsIgnored(string path);
        IEnumerable<string> GetPatterns();
    }
}
