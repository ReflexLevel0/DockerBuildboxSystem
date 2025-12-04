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
    /// Represents the view model for a dropdown user control, providing data binding and value change notification for
    /// dropdown options.
    /// </summary>
    /// <remarks>This view model exposes the dropdown's definition, available values, and the currently
    /// selected value for use in data binding scenarios. It implements property change notification to support UI
    /// updates when the selected value changes. Value changes are propagated to any registered services or callbacks as
    /// appropriate.</remarks>
    public class DropdownViewModel: INotifyPropertyChanged, IUserControlViewModel
    {
        private readonly IUserControlService? _service;
        // Action to update variable values externally
        private readonly Action<string, string>? _updateVariableAction;

        public event PropertyChangedEventHandler? PropertyChanged;
        // Strongly typed definition property
        public DropdownOption Definition { get; }
        // Explicit interface implementation to expose the definition as UserControlDefinition
        UserControlDefinition IUserControlViewModel.Definition => Definition;

        public string? Id => Definition.Id;
        public string? Label => Definition.Label;
        public RangeObservableCollection<string>? Values { get; }

        private string? _selectedValue;
        public string? SelectedValue
        {
            get => _selectedValue;
            set
            {
                if (_selectedValue != value)
                {
                    _selectedValue = value;
                    // Notify property change
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedValue)));
                    // Handle value change logic
                    OnValueChanged(Id, _selectedValue);
                }
            }
        }

        public DropdownViewModel(DropdownOption definition,
                                IUserControlService? service,
                                Action<string, string>? updateVariableAction)
        {
            Definition = definition;
            _service = service;
            _updateVariableAction = updateVariableAction;

            Values = new RangeObservableCollection<string>(definition.Values);
            _selectedValue = definition.Value ?? string.Empty;
            
        }

        /// <summary>
        /// Handles the logic to be executed when the selected value changes.
        /// Saves the updated value to the user control service and invokes the provided
        /// action to update variable values. This is used to keep the shared _userVariables in sync with the UI.
        /// </summary>
        /// <param name="id">the Id of the dropdown whose value changed</param>
        /// <param name="value">the new value of the dropdown</param>
        private void OnValueChanged(string? id, string? value)
        {
            if (!string.IsNullOrEmpty(id) && value != null)
            {
                // Save the variable asynchronously, but don't await to avoid blocking the setter
                _ = _service?.AddUserControlValueAsync(id, value);
                // Notify the listener to update variable values
                _updateVariableAction?.Invoke(id, value);
            }
        }

    }
}
