using Avalonia.Controls;
using LauncherKit.Desktop.ViewModels;

namespace LauncherKit.Desktop;

/// <summary>The consumer's explicit view model to view mapping, used by <see cref="ViewLocator"/>.</summary>
public interface IViewRegistry
{
	/// <summary>
	/// Creates the view for <paramref name="viewModel"/> from the current scope's <paramref name="services"/>; throws
	/// <see cref="InvalidOperationException"/> for a view model without a view.
	/// </summary>
	Control CreateView(ViewModelBase viewModel, IServiceProvider services);
}
