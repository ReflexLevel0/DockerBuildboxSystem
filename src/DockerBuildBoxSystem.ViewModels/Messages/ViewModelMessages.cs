using CommunityToolkit.Mvvm.Messaging.Messages;
using DockerBuildBoxSystem.Contracts;

/*
 https://learn.microsoft.com/en-us/dotnet/communitytoolkit/mvvm/messenger
*/
namespace DockerBuildBoxSystem.ViewModels.Messages
{
    /// <summary>
    /// Message sent when the selected container changes.
    /// </summary>
    public sealed class SelectedImageChangedMessage(ImageInfo? image) : ValueChangedMessage<ImageInfo?>(image);

    /// <summary>
    /// Message sent when the selected container changes.
    /// </summary>
    public sealed class SelectedContainerChangedMessage(ContainerInfo? container) : ValueChangedMessage<ContainerInfo?>(container);

    /// <summary>
    /// Message sent when the command running state changes.
    /// </summary>
    public sealed class IsCommandRunningChangedMessage(bool isRunning) : ValueChangedMessage<bool>(isRunning);

    /// <summary>
    /// Message sent when the sync running state changes.
    /// </summary>
    public sealed class IsSyncRunningChangedMessage(bool isRunning) : ValueChangedMessage<bool>(isRunning);

    /// <summary>
    /// Message sent when the auto-start logs setting changes.
    /// </summary>
    public sealed class AutoStartLogsChangedMessage(bool autoStart) : ValueChangedMessage<bool>(autoStart);

    /// <summary>
    /// Message sent when thee container has been stadted.
    /// </summary>
    public sealed class ContainerStartedMessage(ContainerInfo container) : ValueChangedMessage<ContainerInfo>(container);

    /// <summary>
    /// Message sent when thee container is running.
    /// </summary>
    public sealed class ContainerRunningMessage(ContainerInfo container) : ValueChangedMessage<ContainerInfo>(container);
}

