using Microsoft.Extensions.Configuration;

namespace DockerBuildBoxSystem.App.Config;

/// <summary>
/// Configuration settings for the application.
/// Maps to appsettings.json structure.
/// </summary>
public class AppSettings
{
    public ApplicationSettings Application { get; set; } = new();
}

public class ApplicationSettings
{
    public string Name { get; set; } = "Docker BuildBox System";
    public string Version { get; set; } = "1.0.0";
    public string Environment { get; set; } = "Development";
}

/// <summary>
/// Extension methods for configuration binding.
/// </summary>
public static class ConfigurationExtensions
{
    /// <summary>
    /// Binds the configuration to AppSettings object.
    /// </summary>
    public static AppSettings GetAppSettings(this IConfiguration configuration)
    {
        var settings = new AppSettings();
        configuration.Bind(settings);
        return settings;
    }
}
