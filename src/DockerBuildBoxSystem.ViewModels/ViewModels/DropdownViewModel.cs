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
    /// Represents a view model for a dropdown control that supports data binding
    /// and user variable persistence.
    /// </summary>
    /// <remarks>This class provides property change notifications for data binding scenarios and
    /// automatically persists the selected value using the provided user variable service when the value changes. It is
    /// typically used in MVVM architectures to connect UI dropdowns to application logic.</remarks>
    public class DropdownViewModel: INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        private readonly IUserVariableService? _service;
        public DropdownOption Definition { get; }
        public string Id => Definition.Id ?? string.Empty;
        public string Label => Definition.Label ?? string.Empty;
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
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedValue)));
                    if (!string.IsNullOrEmpty(Id) && _selectedValue != null)
                    {
                        // Save the variable asynchronously, but don't await to avoid blocking the setter
                        _ = _service?.AddUserVariableAsync(Id, _selectedValue ?? "");
                    }
                }
            }
        }

        public DropdownViewModel(DropdownOption definition,
            IUserVariableService? service)
        {
            Definition = definition;
            _service = service;
            Values = new RangeObservableCollection<string>(definition.Values);
            _selectedValue = definition.SelectedValue ?? string.Empty;
        }

    }
}
