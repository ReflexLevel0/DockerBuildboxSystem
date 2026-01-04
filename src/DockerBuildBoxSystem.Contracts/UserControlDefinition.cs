namespace DockerBuildBoxSystem.Contracts
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
    /// Represents a user control for a dropdown menu, allowing users to select from a list of options.
    /// </summary>
    public class DropdownOption : UserControlDefinition
    {
        public string Id { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public List<string> Values { get; set; } = new List<string>();
        public string? Value { get; set; }
    }

    /// <summary>
    /// Represents a command definition for a text box control, including its identifier, display label, and current
    /// value.
    /// </summary>
    /// <remarks>Use this class to configure and represent a text box command within a user interface
    /// definition. The properties allow customization of the control's identity, label, and initial or current
    /// value.</remarks>
    public class TextBoxCommand : UserControlDefinition
    {
        public string? Id { get; set; } = string.Empty;
        public string? Label { get; set; } = string.Empty;
        public string? Value { get; set; }
    }
}
