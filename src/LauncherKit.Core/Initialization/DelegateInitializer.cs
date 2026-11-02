namespace LauncherKit.Core.Initialization;

internal sealed class DelegateInitializer : IConfiguredScopeInitializer, IGlobalInitializer, IEarlyConfiguredScopeInitializer,
	IEarlyGlobalInitializer
{
	private readonly Action<IServiceProvider> _initializer;
	private readonly IServiceProvider _serviceProvider;

	public DelegateInitializer(
		Action<IServiceProvider> initializer, IServiceProvider serviceProvider)
	{
		_initializer = initializer;
		_serviceProvider = serviceProvider;
	}

	public ValueTask Initialize()
	{
		_initializer(_serviceProvider);
		return ValueTask.CompletedTask;
	}
}