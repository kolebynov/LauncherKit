using LauncherKit.Desktop.Activation;

namespace LauncherKit.Desktop.ViewModels;

public abstract class BaseActivatableViewModel : ViewModelBase, IActivatableViewModel
{
	public ViewModelActivator Activator { get; } = new();
}