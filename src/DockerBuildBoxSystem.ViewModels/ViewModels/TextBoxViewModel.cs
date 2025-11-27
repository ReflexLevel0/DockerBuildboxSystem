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
    /// Represents a view model for a text box that supports data binding and user variable persistence.
    /// </summary>
    /// <remarks>This class provides property change notifications for data binding scenarios and
    /// automatically persists the text value using the provided user variable service when the value changes. It is
    /// typically used in MVVM architectures to connect UI text boxes to application logic.</remarks>
    public class TextBoxViewModel: INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        private readonly IUserVariableService? _service;
        public TextBoxCommand Definition { get; }
        public string Id => Definition.Id ?? string.Empty;
        public string Label => Definition.Label ?? string.Empty;
        private string? _value;
        public string? Value
        {
            get => _value;
            set
            {
                if (_value != value)
                {
                    _value = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value)));
                    if (!string.IsNullOrEmpty(Id) && _value != null)
                    {
                        // Save the variable asynchronously, but don't await to avoid blocking the setter
                        _ = _service?.AddUserVariableAsync(Id, _value ?? "");
                    }
                }
            }
        }

        public TextBoxViewModel(TextBoxCommand definition, IUserVariableService? service)
        {
            Definition = definition;
            _service = service;
            _value = definition.Value ?? string.Empty;
        }

    }
}
