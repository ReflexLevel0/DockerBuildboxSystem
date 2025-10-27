using System.Windows;

namespace DockerBuildBoxSystem.App.Services;

/// <summary>
/// Service for locating and creating View instances for ViewModels, centralized way to resolve Views.
/// </summary>
public interface IViewLocator
{
    /// <summary>
    /// Gets the View instance for the specified ViewModel.
    /// </summary>
    /// <param name="viewModel">The ViewModel to get the View for.</param>
    /// <returns>The View instance, or null if not found.</returns>
    Window? GetViewForViewModel(object viewModel);
}

/// <summary>
/// Default implementation of IViewLocator (ViewModel suffix -> View suffix).
/// Ex: MainViewModel -> MainWindow or MainView
/// </summary>
public class ViewLocator : IViewLocator
{
    private readonly IServiceProvider _serviceProvider;

    public ViewLocator(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public Window? GetViewForViewModel(object viewModel)
    {
        if (viewModel == null)
            return null;

        var viewModelType = viewModel.GetType();
        var viewModelName = viewModelType.Name;

        //try to find the corresponding view type (ViewModel -> Window or View)
        var viewName = viewModelName.Replace("ViewModel", "Window");
        var viewType = Type.GetType($"DockerBuildBoxSystem.App.{viewName}");

        if (viewType == null)
        {
            viewName = viewModelName.Replace("ViewModel", "View");
            viewType = Type.GetType($"DockerBuildBoxSystem.App.Views.{viewName}");
        }

        if (viewType == null)
            return null;

        //create instance using dependency injection if possible
        var view = _serviceProvider.GetService(viewType) as Window;
        
        if (view != null)
        {
            view.DataContext = viewModel;
        }

        return view;
    }
}
