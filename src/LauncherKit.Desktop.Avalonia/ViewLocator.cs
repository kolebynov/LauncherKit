using Avalonia.Controls;
using LauncherKit.Desktop.ViewModels;
using Avalonia.Controls.Templates;

namespace LauncherKit.Desktop.Avalonia;

public sealed class ViewLocator : IDataTemplate
{
	private IViewRegistry? _registry;
	private Func<IServiceProvider>? _services;

	public void Configure(IViewRegistry registry, Func<IServiceProvider> services)
	{
		ArgumentNullException.ThrowIfNull(registry);
		ArgumentNullException.ThrowIfNull(services);
		_registry = registry;
		_services = services;
	}

	public Control? Build(object? param)
	{
		if (param is null)
		{
			return null;
		}

		if (_registry is null || _services is null)
		{
			throw new InvalidOperationException("The view locator has not been configured with a view registry and service provider.");
		}

		return _registry.CreateView(param.GetType(), _services());
	}

	public bool Match(object? data) => data is ViewModelBase;
}
