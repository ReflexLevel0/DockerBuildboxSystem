namespace DockerBuildBoxSystem.Models
{

    /// <summary>
    /// Represents a base class for user interface controls, providing common properties for control behavior and
    /// appearance.
    /// </summary>
    /// <remarks>This class serves as a foundation for creating custom user interface controls. It includes
    /// properties for defining the control's identifier and an optional tooltip for additional context or
    /// guidance.</remarks>
    public abstract class UserControlDefinition
    {
        public string Control { get; set; } = string.Empty;
        public string? Tooltip { get; set; }

    }

    /// <summary>
    /// Represents a user control that encapsulates a button with an associated command and optional icon.
    /// </summary>
    /// <remarks>This control is designed to display a button that can execute one or more commands.  An
    /// optional icon can be specified to visually represent the button's purpose.</remarks>
    public class ButtonCommand : UserControlDefinition
    {
        public string[] Command { get; set; } = Array.Empty<string>();
        public string? IconPath { get; set; }

    }

    /// <summary>
    /// Represents an option in a dropdown control, including its identifier, label, available values, and the currently
    /// selected value.
    /// </summary>
    /// <remarks>This class is designed to encapsulate the data required for a dropdown option, such as its
    /// unique identifier, display label, a collection of selectable values, and the currently selected value. It can be
    /// used in UI components to manage dropdown state and behavior.</remarks>
    public class DropdownOption : UserControlDefinition
    {
        public string Id { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public List<string> Values { get; set; } = new List<string>();
        public string? SelectedValue { get; set; }
    }

    /// <summary>
    /// Represents a user control that provides a text box with associated metadata,  including an identifier, a label,
    /// and a value.
    /// </summary>
    /// <remarks>This control is designed to encapsulate a text box along with its metadata,  making it
    /// suitable for scenarios where a labeled input field is required.  The <see cref="Id"/> property can be used to
    /// uniquely identify the control,  while the <see cref="Label"/> provides a descriptive name for the input,  and
    /// the <see cref="Value"/> holds the user-entered or programmatically assigned text.</remarks>
    public class TextBoxCommand : UserControlDefinition
    {
        public string? Id { get; set; } = string.Empty;
        public string? Label { get; set; } = string.Empty;
        public string? Value { get; set; } = string.Empty;

    }
}
