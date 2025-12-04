using DockerBuildBoxSystem.Contracts;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DockerBuildBoxSystem.ViewModels.ViewModels
{
    /// <summary>
    /// Represents the view model for a UI button, providing access to its command definition and related display
    /// properties.
    /// </summary>
    /// <remarks>This class exposes button metadata such as the command to execute, icon path, and tooltip
    /// text, typically for use in data binding scenarios within UI frameworks.</remarks>
    public class ButtonViewModel: IUserControlViewModel
    {
        public ButtonCommand Definition { get; }
        UserControlDefinition IUserControlViewModel.Definition => Definition;
        public string[] Command => Definition.Command;
        public string? IconPath => Definition.IconPath;
        public string? Tooltip => Definition.Tooltip;

        public ButtonViewModel(ButtonCommand model)
        {
            Definition = model;
        }
    }
}
