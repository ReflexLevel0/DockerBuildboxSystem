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
            _viewModel.PropertyChanged += ViewModelOnPropertyChanged;
        }

        private void OnUnloaded(object? sender, RoutedEventArgs e)
        {
            if (_viewModel is null)
                return;

            //Only detach view-specific behavior
            _viewModel.PropertyChanged -= ViewModelOnPropertyChanged;
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
                _viewModel.PropertyChanged -= ViewModelOnPropertyChanged;

                //dispose the ViewModel
                await _viewModel.DisposeAsync();
            }
            catch
            {
                //ignoring errors during cleanup
            }
        }

        private void ViewModelOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (_viewModel is null)
                return;

            switch(e.PropertyName)
            {
                case (nameof(ContainerConsoleViewModel.IsCommandRunning)):
                    //scroll once the command has completed
                    if (!_viewModel.IsCommandRunning)
                        ScrollToLastItem();
                    break;
            }
        }

        private void ScrollToLastItem()
        {
            if (OutputList?.Items.Count is > 0)
            {
                Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
                {
                    if (OutputList.Items.Count == 0) return;
                    var last = OutputList.Items[^1];
                    OutputList.ScrollIntoView(last);
                }));
            }
        }
    }
}
