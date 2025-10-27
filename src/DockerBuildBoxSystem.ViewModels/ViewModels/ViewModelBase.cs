using CommunityToolkit.Mvvm.ComponentModel;

namespace DockerBuildBoxSystem.ViewModels.Common;

/// <summary>
/// Base class for all ViewModels in the application, uses CommunityToolkit.Mvvm.
/// </summary>
public abstract class ViewModelBase : ObservableObject
{
    private bool _isBusy;
    private string _title = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether the ViewModel is currently busy, showing loading indicators in the UI.
    /// </summary>
    public bool IsBusy
    {
        get => _isBusy;
        set => SetProperty(ref _isBusy, value);
    }

    /// <summary>
    /// Gets or sets the title for the ViewModel.
    /// </summary>
    public string Title
    {
        get => _title;
        set => SetProperty(ref _title, value);
    }
}
