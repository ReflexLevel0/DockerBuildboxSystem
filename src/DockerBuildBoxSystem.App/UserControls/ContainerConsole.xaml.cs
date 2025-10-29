using DockerBuildBoxSystem.Contracts;
using DockerBuildBoxSystem.Domain;
using DockerBuildBoxSystem.ViewModels.ViewModels;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
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
            
            //resolve ViewModel from DI if not provided
            if (viewModel == null)
            {
                viewModel = ((App)Application.Current).Services.GetRequiredService<ContainerConsoleViewModel>();
            }
            
            DataContext = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
            
            //subscribe to Lines collection changes to auto-scroll
            viewModel.Lines.CollectionChanged += Lines_CollectionChanged;
        }

        public ContainerConsoleViewModel? ViewModel => DataContext as ContainerConsoleViewModel;

        private void Lines_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            //auto-scroll to the bottom when new lines are added
            if (e.Action == NotifyCollectionChangedAction.Add)
                OutputScroller.ScrollToBottom();
        }

        #region Bindable Config
        public static readonly DependencyProperty ContainerIdProperty =
            DependencyProperty.Register(nameof(ContainerId), typeof(string), typeof(ContainerConsole), 
                new PropertyMetadata("", OnContainerIdChanged));

        public string ContainerId
        {
            get => (string)GetValue(ContainerIdProperty);
            set => SetValue(ContainerIdProperty, value);
        }

        private static void OnContainerIdChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is ContainerConsole console && console.ViewModel is not null)
            {
                console.ViewModel.ContainerId = e.NewValue as string ?? "";
            }
        }

        public static readonly DependencyProperty TailProperty =
            DependencyProperty.Register(nameof(Tail), typeof(string), typeof(ContainerConsole), 
                new PropertyMetadata("50", OnTailChanged));

        public string Tail
        {
            get => (string)GetValue(TailProperty);
            set => SetValue(TailProperty, value);
        }

        private static void OnTailChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is ContainerConsole console && console.ViewModel is not null)
            {
                console.ViewModel.Tail = e.NewValue as string ?? "50";
            }
        }

        public static readonly DependencyProperty TtyProperty =
            DependencyProperty.Register(nameof(Tty), typeof(bool), typeof(ContainerConsole), 
                new PropertyMetadata(false, OnTtyChanged));

        public bool Tty
        {
            get => (bool)GetValue(TtyProperty);
            set => SetValue(TtyProperty, value);
        }

        private static void OnTtyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is ContainerConsole console && console.ViewModel is not null)
            {
                console.ViewModel.Tty = (bool)e.NewValue;
            }
        }

        public static readonly DependencyProperty AutoStartLogsProperty =
            DependencyProperty.Register(nameof(AutoStartLogs), typeof(bool), typeof(ContainerConsole), 
                new PropertyMetadata(true));

        public bool AutoStartLogs
        {
            get => (bool)GetValue(AutoStartLogsProperty);
            set => SetValue(AutoStartLogsProperty, value);
        }
        #endregion
        private async void OnLoaded(object? sender, RoutedEventArgs e)
        {
            if (ViewModel is null) return;

            //set initial values from dependency properties
            ViewModel.ContainerId = ContainerId ?? "";
            ViewModel.Tail = Tail ?? "50";
            ViewModel.Tty = Tty;

            //load available containers
            await ViewModel.RefreshContainersCommand.ExecuteAsync(null);

            if (AutoStartLogs && !string.IsNullOrWhiteSpace(ViewModel.ContainerId))
            {
                await ViewModel.StartLogsCommand.ExecuteAsync(null);
            }
        }

        private async void OnUnloaded(object? sender, RoutedEventArgs e)
        {
            if (ViewModel is not null)
            {
                //unsubscribe from events
                ViewModel.Lines.CollectionChanged -= Lines_CollectionChanged;
                
                if (ViewModel.StopLogsCommand.CanExecute(null))
                {
                    await ViewModel.StopLogsCommand.ExecuteAsync(null);
                }
            }
        }
    }
}
