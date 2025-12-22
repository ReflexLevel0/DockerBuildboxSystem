using CommunityToolkit.Mvvm.Messaging.Messages;
using DockerBuildBoxSystem.Contracts;

namespace DockerBuildBoxSystem.ViewModels.Common
{
    // Message published when the selected container has started running
    public sealed class ContainerRunningMessage : ValueChangedMessage<ContainerInfo>
    {
        public ContainerRunningMessage(ContainerInfo value) : base(value) { }
    }
}
