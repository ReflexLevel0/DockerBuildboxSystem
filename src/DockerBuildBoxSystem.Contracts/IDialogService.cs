using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DockerBuildBoxSystem.Contracts
{
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
    }
}
