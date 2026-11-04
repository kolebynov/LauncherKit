using Avalonia.Controls;
using LauncherKit.Desktop.ViewModels;
using Avalonia.Controls.Templates;

namespace LauncherKit.Desktop;

/// <summary>
/// The application-wide data template for view models: creates each view through the consumer's
/// <see cref="IViewRegistry"/> with the services of the scope current at that moment.
/// </summary>
public sealed class ViewLocator(IViewRegistry registry, Func<IServiceProvider> services) : IDataTemplate
{
	public Control? Build(object? param) => param switch
	{
		null => null,
		ViewModelBase viewModel => registry.CreateView(viewModel, services()),
		_ => throw new ArgumentException($"'{param.GetType().FullName}' is not a view model.", nameof(param)),
	};

	public bool Match(object? data) => data is ViewModelBase;
}
