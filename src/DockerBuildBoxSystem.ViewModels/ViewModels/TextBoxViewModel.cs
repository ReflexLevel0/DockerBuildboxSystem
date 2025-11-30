using DockerBuildBoxSystem.Contracts;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DockerBuildBoxSystem.ViewModels.ViewModels
{
    /// <summary>
    /// Represents the view model for a text box user control, providing data binding and change notification for its
    /// value.
    /// </summary>
    /// <remarks>Implements property change notification to support data binding scenarios in UI frameworks.
    /// The view model exposes the text box definition and manages updates to its value, propagating changes to
    /// associated services or actions as needed.</remarks>

    public class TextBoxViewModel: INotifyPropertyChanged, IUserControlViewModel
    {
        private readonly IUserControlService? _service;
        // Action to update variable values externally
        private readonly Action<string, string>? _updateVariableAction;

        public TextBoxCommand Definition { get; }
        // Explicit interface implementation to expose the Definition as UserControlDefinition
        UserControlDefinition IUserControlViewModel.Definition => Definition;

        public event PropertyChangedEventHandler? PropertyChanged;
        
        public string? Id => Definition.Id;
        public string? Label => Definition.Label;
        private string? _value;
        public string? Value
        {
            get => _value;
            set
            {
                if (_value != value)
                {
                    _value = value;
                    // Notify property change
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value)));
                    // Handle value change logic
                    OnValueChanged(Id, _value);
                }
            }
        }

        public TextBoxViewModel(TextBoxCommand definition,
                                IUserControlService? service,
                                Action<string, string>? updateVariableAction)
        {
            Definition = definition;
            _service = service;
            _updateVariableAction = updateVariableAction;

            _value = definition.Value ?? string.Empty;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="id"></param>
        /// <param name="value"></param>
        private void OnValueChanged(string? id, string? value)
        {
            if (!string.IsNullOrEmpty(id) && value != null)
            {
                // Save the variable asynchronously, but don't await to avoid blocking the setter
                _ = _service?.AddUserControlValueAsync(id, value);
                _updateVariableAction?.Invoke(id, value);
            }
        }

    }
}
