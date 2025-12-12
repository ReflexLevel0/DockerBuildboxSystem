using CommunityToolkit.Mvvm.Input;
using DockerBuildBoxSystem.Contracts;
using DockerBuildBoxSystem.ViewModels.Common;
using System;
using System.Threading.Tasks;

namespace DockerBuildBoxSystem.ViewModels.ViewModels
{
    public sealed partial class ContainerConsoleViewModel
    {
        /// <summary>
        /// Determines whether sending commands is currently allowed.
        /// </summary>
        private bool CanSend() => !string.IsNullOrWhiteSpace(ContainerId) && (SelectedContainer?.IsRunning == true) && !IsSyncRunning && !IsSwitching;


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
        private async Task RunUserCommandAsync(UserControlDefinition? control)
        {
            // Check for null control or no selected container
            if (control is null || SelectedContainer is null)
            {
                PostLogMessage("[user-cmd] No command or container selected.", true);
                return;
            }
            // Handle ButtonCommand control type
            if (control is ButtonCommand buttonCmd)
            {
                var cmds = buttonCmd.Command;
                if (cmds.Length == 0)
                {
                    PostLogMessage("[user-cmd] No command defined for this button.", true);
                    return;
                }
                var cmdStr = string.Join(' ', cmds);
                await RouteInputAsync(cmdStr);
            }
            else
            {
                PostLogMessage("[user-cmd] Unsupported control type for command execution.", true);
            }
        }

        /// <summary>
        /// Sends the current input text as a command to the container.
        /// </summary>
        [RelayCommand(CanExecute = nameof(CanSend), AllowConcurrentExecutions = true)]
        private async Task SendAsync()
        {
            var raw = (Input ?? string.Empty);
            Input = string.Empty;

            await RouteInputAsync(raw);
        }

        /// <summary>
        /// Executes a command string by splitting it as a argv array.
        /// </summary>
        private async Task ExecuteAndLogAsync(string cmd) => await ExecuteAndLog(ShellSplitter.SplitShellLike(cmd));

        /// <summary>
        /// Executes a command inside the selected container and streams output to the console UI.
        /// </summary>
        /// <param name="args">The command and its arguments (argv form).</param>
        private Task ExecuteAndLog(string[] args)
        {
            //add command to console on UI thread
            PostLogMessage($"> {string.Join(' ', args)}", false);

            return Task.Run(async () =>
            {
                try
                {
                    await foreach (var (isErr, line) in _cmdRunner.RunAsync(_service, ContainerId, args).ConfigureAwait(false))
                    {
                        UIHandler.EnqueueLine(line, isErr);
                    }

                    var exitCode = await _cmdRunner.ExitCode.ConfigureAwait(false);
                    PostLogMessage($"[exit] {exitCode}", false, true);
                }
                catch (OperationCanceledException)
                {
                    PostLogMessage("[exec] canceled", false, true);
                }
                catch (Exception ex)
                {
                    PostLogMessage($"[exec-error] {ex.Message}", true, true);
                }
            });
        }

        /// <summary>
        /// Determines whether command execution can be stopped.
        /// </summary>
        private bool CanStopExec() => _cmdRunner.IsRunning;

        /// <summary>
        /// Stops the current command execution task, if it is running.
        /// </summary>
        [RelayCommand(CanExecute = nameof(CanStopExec))]
        private async Task StopExecAsync()
        {
            await _cmdRunner.StopAsync();
        }

        /// <summary>
        /// Determines whether command execution can be interrupted...
        /// </summary>
        private bool CanInterruptExec() => _cmdRunner.IsRunning;

        /// <summary>
        /// Interrupts the current command execution task, if it is running!
        /// </summary>
        [RelayCommand(CanExecute = nameof(CanInterruptExec))]
        private async Task InterruptExecAsync()
        {
            await _cmdRunner.InterruptAsync();
        }
        
        private async Task RouteInputAsync(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return;

            // resolve user variables from input
            var resolvedCommand = await _userControlService.RetrieveVariableAsync(raw, _userVariables);

            if (await _cmdRunner.TryWriteToInteractiveAsync(resolvedCommand))
                return;

            var args = ShellSplitter.SplitShellLike(resolvedCommand);
            await ExecuteAndLog(args);
        }
    }
}
