using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DockerBuildBoxSystem.Contracts;
using DockerBuildBoxSystem.ViewModels.Common;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace DockerBuildBoxSystem.ViewModels.ViewModels
{
    public partial class CommandExecutionViewModel : ViewModelBase
    {
        private readonly ICommandRunner _cmdRunner;
        private readonly IContainerService _service;
        private readonly IUserControlService _userControlService;
        private readonly IViewModelLogger _logger;
        private readonly UserControlsViewModel _userControlsViewModel;

        private string ContainerId
        {
            get => SelectedContainer?.Id ?? string.Empty;
        }

        /// <summary>
        /// The selected container info object.
        /// </summary>
        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(SendCommand))]
        [NotifyCanExecuteChangedFor(nameof(RunUserCommandCommand))]
        private ContainerInfo? _selectedContainer;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(SendCommand))]
        [NotifyCanExecuteChangedFor(nameof(RunUserCommandCommand))]
        private bool _isSyncRunning;

        [ObservableProperty]
        private string? _input;

        private bool IsContainerRunning => SelectedContainer?.IsRunning == true;
        public bool IsCommandRunning => _cmdRunner.IsRunning;

        public CommandExecutionViewModel(
            ICommandRunner cmdRunner,
            IContainerService service,
            IUserControlService userControlService,
            IViewModelLogger logger,
            UserControlsViewModel userControlsViewModel)
        {
            _cmdRunner = cmdRunner ?? throw new ArgumentNullException(nameof(cmdRunner));
            _service = service ?? throw new ArgumentNullException(nameof(service));
            _userControlService = userControlService ?? throw new ArgumentNullException(nameof(userControlService));
            _userControlsViewModel = userControlsViewModel ?? throw new ArgumentNullException(nameof(userControlsViewModel));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            _cmdRunner.RunningChanged += (_, __) =>
            {
                SetOnUiThread(() =>
                {
                    OnPropertyChanged(nameof(IsCommandRunning));
                    SendCommand.NotifyCanExecuteChanged();
                    RunUserCommandCommand.NotifyCanExecuteChanged();
                    StopExecCommand.NotifyCanExecuteChanged();
                    InterruptExecCommand.NotifyCanExecuteChanged();
                });
            };
        }

        private bool CanSend() => !string.IsNullOrWhiteSpace(ContainerId) && IsContainerRunning;
        
        /// <summary>
        /// Sends the current input text as a command to the container.
        /// </summary>
        [RelayCommand(CanExecute = nameof(CanSend), AllowConcurrentExecutions = true)]
        public async Task SendAsync()
        {
            var raw = (Input ?? string.Empty);
            Input = string.Empty;

            await RouteInputAsync(raw);
        }

        /// <summary>
        /// Executes a user-defined command associated with the specified control asynchronously.
        /// </summary>
        /// <remarks>If the <paramref name="control"/> is <see langword="null"/> or no container is
        /// selected, the method logs a message indicating that no command or container is available. If the control is
        /// a <see cref="ButtonCommand"/> and no command is defined for the button, a corresponding message is logged.
        /// Unsupported control types are also logged.</remarks>
        /// <param name="control">The <see cref="UserControlDefinition"/> representing the control that defines the command to execute. This
        /// parameter can be <see langword="null"/>.</param>
        /// <returns></returns>
        [RelayCommand(CanExecute = nameof(CanSend))]
        public async Task RunUserCommandAsync(UserControlDefinition? control)
        {
            // Check for null control or no selected container
            if (control is null || SelectedContainer is null)
            {
                _logger.LogWithNewline("[user-cmd] No command or container selected.", true);
                return;
            }
            // Handle ButtonCommand control type
            if (control is ButtonCommand buttonCmd)
            {
                var cmds = buttonCmd.Command;
                if (cmds.Length == 0)
                {
                    _logger.LogWithNewline("[user-cmd] No command defined for this button.", true);
                    return;
                }
                var cmdStr = string.Join(' ', cmds);
                await RouteInputAsync(cmdStr);
            }
            else
            {
                _logger.LogWithNewline("[user-cmd] Unsupported control type for command execution.", true);
            }
        }

        private async Task RouteInputAsync(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return;

            // resolve user variables from input
            var resolvedCommand = await _userControlService.RetrieveVariableAsync(raw, _userControlsViewModel.UserVariables);

            if (await _cmdRunner.TryWriteToInteractiveAsync(resolvedCommand))
                return;

            var args = ShellSplitter.SplitShellLike(resolvedCommand);
            await ExecuteAndLog(args);
        }

        partial void OnSelectedContainerChanged(ContainerInfo? oldValue, ContainerInfo? newValue)
        {
            var switchedContainer = oldValue?.Id != newValue?.Id;

            //if selection change didn't actually switch container, keep current exec session.
            if (!switchedContainer)
                return;

            //stop exec session from the previous container.
            if (_cmdRunner.IsRunning && StopExecCommand.CanExecute(null))
                StopExecCommand.Execute(null);
        }

        /// <summary>
        /// Executes a command inside the selected container and streams output to the console UI.
        /// </summary>
        /// <param name="args">The command and its arguments (argv form).</param>
        private async Task ExecuteAndLog(string[] args)
        {
            //add command to console on UI thread
            _logger.LogWithNewline($"> {string.Join(' ', args)}");

            await Task.Run(async () =>
            {
                try
                {
                    await foreach (var (isErr, line) in _cmdRunner.RunAsync(_service, ContainerId, args).ConfigureAwait(false))
                    {
                        _logger.Log(line, isErr);
                    }

                    var exitCode = await _cmdRunner.ExitCode.ConfigureAwait(false);
                    _logger.LogWithNewline($"[exit] {exitCode}", isImportant: true);
                }
                catch (OperationCanceledException)
                {
                    _logger.LogWithNewline("[exec] canceled", isImportant: true);
                }
                catch (Exception ex)
                {
                    _logger.LogWithNewline($"[exec-error] {ex.Message}", true, true);
                }
            });
        }

        private bool CanStartShell() => !string.IsNullOrWhiteSpace(ContainerId) && IsContainerRunning && !_cmdRunner.IsRunning;

        /// <summary>
        /// Starts an interactive bash shell session inside the selected container.
        /// </summary>
        [RelayCommand(CanExecute = nameof(CanStartShell))]
        public async Task StartShellAsync()
        {
            // Prefer interactive login shell
            await ExecuteAndLog(new[] { "/bin/bash", "-i" });
        }

        private bool CanStopExec() => _cmdRunner.IsRunning;

        [RelayCommand(CanExecute = nameof(CanStopExec))]
        public async Task StopExecAsync()
        {
            await _cmdRunner.StopAsync();
        }

        private bool CanInterruptExec() => _cmdRunner.IsRunning;

        [RelayCommand(CanExecute = nameof(CanInterruptExec))]
        public async Task InterruptExecAsync()
        {
            await _cmdRunner.InterruptAsync();
        }

        public override async ValueTask DisposeAsync()
        {
            await StopExecAsync();
            await base.DisposeAsync();
        }
    }
}
