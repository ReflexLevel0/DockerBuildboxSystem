using CommunityToolkit.Mvvm.Messaging.Messages;
using DockerBuildBoxSystem.Contracts;

namespace DockerBuildBoxSystem.ViewModels.Common
{
    public sealed class ContainerReadyMessage : ValueChangedMessage<ContainerInfo>
    {
        public ContainerReadyMessage(ContainerInfo value) : base(value) { }
    }
}
