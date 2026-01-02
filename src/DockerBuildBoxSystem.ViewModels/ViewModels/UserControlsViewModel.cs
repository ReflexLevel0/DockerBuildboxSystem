using DockerBuildBoxSystem.Contracts;
using DockerBuildBoxSystem.ViewModels.Common;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DockerBuildBoxSystem.ViewModels.ViewModels
{
    public class UserControlsViewModel : ViewModelBase
    {
        private readonly IUserControlService _userControlService;
        private readonly IViewModelLogger _logger;
        private readonly int _maxControls = 15;
        private List<UserVariables> _userVariables = new();
        public List<UserVariables> UserVariables => _userVariables;
        public ObservableCollection<IUserControlViewModel> UserControls { get; } = new();
        public UserControlsViewModel(IUserControlService userControlService, IViewModelLogger logger)
        {
            _userControlService = userControlService ?? throw new ArgumentNullException(nameof(userControlService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Load the user-defined controls from the service and populate the ViewModel collection.
        /// then loads any saved user variable values.
        /// </summary>
        /// <remarks>If the number of loaded controls exceeds the maximum allowed,
        /// only the first <paramref name="maxControls"/> will be used.</remarks>
        /// <returns> a task representing the asynchronous operation</returns>
        public async Task LoadUserControlsAsync()
        {
            var controls = await _userControlService.LoadUserControlsAsync()
                           ?? new List<UserControlDefinition>();

            if (controls.Count > _maxControls)
            {
                _logger.LogWithNewline($"[user-control] Warning: Loaded controls exceed maximum of {_maxControls}. Only the first {_maxControls} will be used.", true, false);
                controls = controls.Take(_maxControls).ToList();
            }

            UserControls.Clear();
            foreach (var control in controls)
            {
                AddControlToViewModel(control);
            }
            // After loading controls, load user variables
            LoadUserVariables();
        }

        /// <summary>
        /// Adds a user control definition to the ViewModel collection by creating the appropriate ViewModel instance.
        /// </summary>
        /// <remarks> Uses updateVarAction to update user variable values when controls change.
        /// this ensures that the shared _userVariables list stays in sync with the UI.</remarks>
        /// <param name="control"> the user control definition</param>
        private void AddControlToViewModel(UserControlDefinition control)
        {
            Action<string, string>? updateVarAction = (id, value) =>
            {
                var existingVar = _userVariables.FirstOrDefault(v => v.Id == id);
                if (existingVar != null)
                    existingVar.Value = value;

                else
                    _userVariables.Add(new UserVariables(id, value));

            };

            // Create appropriate ViewModel based on control type
            switch (control)
            {
                case TextBoxCommand tb:
                    UserControls.Add(new TextBoxViewModel(tb, _userControlService, updateVarAction));
                    break;
                case DropdownOption dd:
                    UserControls.Add(new DropdownViewModel(dd, _userControlService, updateVarAction));
                    break;

                // Handle ButtonCommand with icon path resolution
                case ButtonCommand btn:
                    if (!string.IsNullOrEmpty(btn.IconPath))
                    {
                        btn.IconPath = ResolveIconPath(btn.IconPath, btn.Control);
                    }
                    UserControls.Add(new ButtonViewModel(btn));
                    break;
                default:
                    _logger.LogWithNewline($"[user-control] Warning: Unsupported control type: {control.GetType().Name}", true, false);
                    break;
            }
        }

        /// <summary>
        /// Loads user-defined variable values and updates the corresponding user control view models with the retrieved
        /// data.
        /// </summary>
        /// <remarks>This method synchronizes the values of user controls with the latest user variable
        /// data. It should be called whenever user variables need to be refreshed or reloaded to ensure that the UI
        /// reflects the current state.</remarks>
        private void LoadUserVariables()
        {
            // Retrieve user variables for all defined controls
            var controls = UserControls.Select(vm => vm.Definition).ToList();
            // Load saved user variable values
            _userVariables = _userControlService.LoadUserVariables(controls);

            // Update each control's ViewModel with the loaded variable values
            foreach (var control in UserControls)
            {
                switch (control)
                {
                    case TextBoxViewModel tbVm:
                        var tbVar = _userVariables.FirstOrDefault(v => v.Id == tbVm.Id);
                        if (tbVar != null)
                        {
                            tbVm.Value = tbVar.Value;
                        }
                        break;
                    case DropdownViewModel ddVm:
                        var ddVar = _userVariables.FirstOrDefault(v => v.Id == ddVm.Id);
                        if (ddVar != null)
                        {
                            ddVm.SelectedValue = ddVar.Value;
                        }
                        break;
                }
            }
        }

        /// <summary>
        /// Resolves a relative icon path to an absolute URI.
        /// </summary>
        /// <param name="path"> the relative path to the icon file</param>
        /// <param name="controlName"> the name of the control</param>
        /// <returns> the absolute URI of the icon file, or null if not found</returns>
        private string? ResolveIconPath(string path, string controlName)
        {
            var iconFullPath = Path.Combine(AppContext.BaseDirectory, path.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(iconFullPath))
            {
                // Update to absolute URI format
                return new Uri(iconFullPath, UriKind.Absolute).AbsoluteUri;
            }
            else
            {
                _logger.LogWithNewline($"[user-control] Warning: Icon file not found for button '{controlName}': {iconFullPath}", true, false);
                return null; //clear invalid path
            }
        }
    }
}
