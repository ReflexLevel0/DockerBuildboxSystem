using System.Windows;

namespace DockerBuildBoxSystem.App.Services;

/// <summary>
/// Service for showing dialogs. Abstracts MessageBox and other dialog functionality from ViewModels.
/// </summary>
public interface IDialogService
{
    /// <summary>
    /// Shows an information message.
    /// </summary>
    void ShowInformation(string message, string title = "Information");

    /// <summary>
    /// Shows a warning message.
    /// </summary>
    void ShowWarning(string message, string title = "Warning");

    /// <summary>
    /// Shows an error message.
    /// </summary>
    void ShowError(string message, string title = "Error");

    /// <summary>
    /// Shows a confirmation dialog.
    /// </summary>
    /// <returns>True if user clicked Yes, false otherwise.</returns>
    bool ShowConfirmation(string message, string title = "Confirm");

    /// <summary>
    /// Shows a question dialog with Yes/No/Cancel options.
    /// </summary>
    MessageBoxResult ShowQuestion(string message, string title = "Question");
}

/// <summary>
/// Default implementation of IDialogService using WPF MessageBox.
/// </summary>
public class DialogService : IDialogService
{
    public void ShowInformation(string message, string title = "Information")
    {
        MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);
    }

    public void ShowWarning(string message, string title = "Warning")
    {
        MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    public void ShowError(string message, string title = "Error")
    {
        MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);
    }

    public bool ShowConfirmation(string message, string title = "Confirm")
    {
        var result = MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question);
        return result == MessageBoxResult.Yes;
    }

    public MessageBoxResult ShowQuestion(string message, string title = "Question")
    {
        return MessageBox.Show(message, title, MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
    }
}
