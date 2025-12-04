using DockerBuildBoxSystem.Contracts;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DockerBuildBoxSystem.ViewModels.ViewModels
{
    /// <summary>
    /// Represents a view model that provides metadata and configuration for a user control within the application.
    /// </summary>
    public interface IUserControlViewModel
    {
        UserControlDefinition Definition { get; }
    }
}
