using CommunityToolkit.Mvvm.ComponentModel;

namespace DockerBuildBoxSystem.ViewModels.Common;

/// <summary>
/// Base class for all ViewModels in the application, uses CommunityToolkit.Mvvm.
/// </summary>
public abstract partial class ViewModelBase : ObservableObject, IAsyncDisposable
{
    /// <summary>
    /// Gets or sets a value indicating whether the ViewModel is currently busy, showing loading indicators in the UI.
    /// </summary>
    [ObservableProperty]
    private bool isBusy;

    /// <summary>
    /// Gets or sets the title for the ViewModel.
    /// </summary>
    [ObservableProperty]
    private string title = string.Empty;

    [ObservableProperty]
    private bool isActivated;

    protected readonly SynchronizationContext? syncContext;

    public virtual Task OnActivatedAsync() => Task.CompletedTask;
    public virtual Task OnDeactivatedAsync() => Task.CompletedTask;

    /// <summary>
    /// Disposes of the ViewModel asynchronously. Override this method in derived classes to perform cleanup of async resources.
    /// </summary>
    /// <returns>A ValueTask representing the asynchronous disposal operation.</returns>
    public virtual ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        return ValueTask.CompletedTask;
    }



    /// <summary>
    /// Executes an action on the UI thread IF a synchronization context is available, otherwise executes it inline.
    /// </summary>
    protected void SetOnUiThread(Action action)
    {
        if (syncContext == null || SynchronizationContext.Current == syncContext)
        {
            action();
            return;
        }

        syncContext.Post(_ =>
        {
            try
            {
                action();
            }
            catch (InvalidOperationException)
            {

            }
        }, null);
    }

    public ViewModelBase()
    {
        syncContext = SynchronizationContext.Current;
    }
}
