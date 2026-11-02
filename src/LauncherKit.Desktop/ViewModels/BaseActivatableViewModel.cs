using LauncherKit.Desktop.Activation;
using LauncherKit.Desktop.ViewModels;

namespace LauncherKit.Desktop.ViewModels;

public abstract class BaseActivatableViewModel : ViewModelBase, IActivatableViewModel
{
	public ViewModelActivator Activator { get; } = new();
}