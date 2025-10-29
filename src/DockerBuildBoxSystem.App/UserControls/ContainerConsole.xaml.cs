using DockerBuildBoxSystem.Contracts;
using DockerBuildBoxSystem.Domain;
using DockerBuildBoxSystem.ViewModels.ViewModels;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using Microsoft.Extensions.DependencyInjection;

namespace DockerBuildBoxSystem.App.UserControls
{

    /// <summary>
    /// logic for ContainerConsole.xaml
    /// </summary>
    public partial class ContainerConsole : UserControl
    {
        public ContainerConsoleViewModel? ViewModel => DataContext as ContainerConsoleViewModel;
        private static bool IsInDesignMode => DesignerProperties.GetIsInDesignMode(new DependencyObject());

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

            DataContext = viewModel;
        }

        private async void OnLoaded(object? sender, RoutedEventArgs e)
        {
            if (ViewModel is null) return;

            //attach auto-scroll behavior
            ViewModel.Lines.CollectionChanged += Lines_CollectionChanged;

            //innitialize the ViewModel
            await ViewModel.InitializeCommand.ExecuteAsync(null);
        }

        private async void OnUnloaded(object? sender, RoutedEventArgs e)
        {
            if (ViewModel is not null)
            {
                //detach the auto-scroll behavior
                ViewModel.Lines.CollectionChanged -= Lines_CollectionChanged;

                //stop logs if running
                if (ViewModel.StopLogsCommand.CanExecute(null))
                {
                    await ViewModel.StopLogsCommand.ExecuteAsync(null);
                }
            }
        }

        private void Lines_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            //auto-scroll to the bottom when new lines are added
            if (e.Action == NotifyCollectionChangedAction.Add)
                OutputScroller.ScrollToBottom();
        }
    }
}
