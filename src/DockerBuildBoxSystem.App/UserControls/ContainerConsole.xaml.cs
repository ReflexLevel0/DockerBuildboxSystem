using DockerBuildBoxSystem.ViewModels.Common;
using DockerBuildBoxSystem.ViewModels.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace DockerBuildBoxSystem.App.UserControls
{

    /// <summary>
    /// logic for ContainerConsole.xaml
    /// </summary>
    public partial class ContainerConsole : UserControl
    {
        private readonly ContainerConsoleViewModel? _viewModel;
        private bool IsInDesignMode => DesignerProperties.GetIsInDesignMode(this);

        /// <summary>
        /// Parameterless constructor.
        /// Resolves the ViewModel from the dependency injection (DI) container.
        /// Required for XAML instantiation (user control)
        /// </summary>
        public ContainerConsole() : this(null)
        {
        }

        /// <summary>
        /// Constructor with ViewModel injection for manual instantiation.
        /// </summary>
        /// <param name="viewModel">The ViewModel to use, or null to resolve from the DI container.</param>
        public ContainerConsole(ContainerConsoleViewModel? viewModel)
        {
            InitializeComponent();

            //resolve ViewModel from DI if not provided AND not in design mode
            if(IsInDesignMode)
                return;

            viewModel ??= (Application.Current as App)?.Services.GetService<ContainerConsoleViewModel>();

            if(viewModel is null)
                throw new InvalidOperationException("ContainerConsoleViewModel could not be resolved from the service provider!");

            viewModel.SetSynchronizationContext(SynchronizationContext.Current);
            _viewModel = viewModel;

            DataContext = _viewModel;

            Loaded += async (s, e) => await viewModel.InitializeCommand.ExecuteAsync(null);
        }


        private void OnLoaded(object? sender, RoutedEventArgs e)
        {
            if (IsInDesignMode || _viewModel is null)
                return;

            if (!ReferenceEquals(DataContext, _viewModel))
                DataContext = _viewModel;

            //attach auto-scroll behavior
            _viewModel.UIHandler.ImportantLineArrived += ViewModelOnImportantLineArrived;
            _viewModel.UIHandler.OutputChunk += _viewModel_OutputChunk;
            _viewModel.UIHandler.OutputCleared += _viewModel_OutputCleared;
            _viewModel.UIHandler.OutputTrimmed += _viewModel_OutputTrimmed;
        }

        /// <summary>
        /// Handles the event triggered when the output is cleared in the view model.
        /// </summary>
        private void _viewModel_OutputCleared(object? sender, EventArgs e)
        {
            TerminalOutput.Clear();
        }

        /// <summary>
        /// Handles the output of a chunk of text from the view model and appends it to the terminal output.
        /// </summary>
        private void _viewModel_OutputChunk(object? sender, string chunk)
        {
            TerminalOutput.AppendText(chunk);
            AutoScrollToEnd(); //seems to work without any discrepancies in performance! :)
        }

        /// <summary>
        /// Handles the event triggered when the output needs to be trimmed by a specified number of characters.
        /// </summary>
        private void _viewModel_OutputTrimmed(object? sender, int charsToRemove)
        {
            if (TerminalOutput is null || charsToRemove <= 0)
                return;

            Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() =>
            {
                if (TerminalOutput.Text.Length >= charsToRemove)
                {
                    TerminalOutput.Text = TerminalOutput.Text.Substring(charsToRemove);
                }
            }));
        }

        private void OnUnloaded(object? sender, RoutedEventArgs e)
        {
            if (_viewModel is null)
                return;

            //Only detach view-specific behavior
            _viewModel.UIHandler.ImportantLineArrived -= ViewModelOnImportantLineArrived;
            _viewModel.UIHandler.OutputChunk -= _viewModel_OutputChunk;
            _viewModel.UIHandler.OutputCleared -= _viewModel_OutputCleared;
            _viewModel.UIHandler.OutputTrimmed -= _viewModel_OutputTrimmed;
        }


        /// <summary>
        /// Performs cleanup operations for the control.
        /// </summary>
        public async Task CleanupAsync()
        {
            if (_viewModel is null)
                return;

            try
            {
                //detach event handlers
                _viewModel.UIHandler.ImportantLineArrived -= ViewModelOnImportantLineArrived;
                _viewModel.UIHandler.OutputChunk -= _viewModel_OutputChunk;
                _viewModel.UIHandler.OutputCleared -= _viewModel_OutputCleared;
                _viewModel.UIHandler.OutputTrimmed -= _viewModel_OutputTrimmed;

                //dispose the ViewModel
                await _viewModel.DisposeAsync();
            }
            catch
            {
                //ignoring errors during cleanup
            }
        }
        private void ViewModelOnImportantLineArrived(object? sender, ConsoleLine line)
        {
            AutoScrollToEnd();
            //Do stuff if important lines arrives... ex show error in popup/notifcation etc
        }

        private void AutoScrollToEnd()
        {
            if (TerminalOutput is null) return;

            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                TerminalOutput.CaretIndex = TerminalOutput.Text.Length;
                TerminalOutput.ScrollToEnd();
            }));
        }
    }
}
