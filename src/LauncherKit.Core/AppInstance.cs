using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using LauncherKit.Core.Initialization;
using LauncherKit.Infrastructure;
using Microsoft.Extensions.Configuration;

namespace LauncherKit.Core;

/// <summary>The built service provider and its replaceable application scope; owned by the kit's application host.</summary>
internal sealed class AppInstance : IDisposable
{
	private readonly ILogger<AppInstance> _logger;
	private IServiceScope? _currentScope;

	public IServiceProvider ServiceProvider => _currentScope!.ServiceProvider;

	public IServiceProvider RootServiceProvider { get; }

	public ConfigurationManager Configuration { get; }

	internal AppInstance(IServiceProvider rootServiceProvider, ConfigurationManager configuration)
	{
		RootServiceProvider = rootServiceProvider;
		Configuration = configuration;
		_logger = rootServiceProvider.GetRequiredService<ILogger<AppInstance>>();
	}

	public void Initialize()
	{
		_logger.LogInformation("Initialization...");

		try
		{
			RunGlobalInitializers();
			CreateNewScope();
			_logger.LogInformation("Initialized");
		}
		catch (Exception e)
		{
			_logger.LogError(e, "Failed to initialize");
			throw;
		}
	}

	/// <summary>Creates the application scope for the designer, without running any initializers.</summary>
	public void InitializeDesignMode() => _currentScope = RootServiceProvider.CreateScope();

	public void CreateNewScope()
	{
		_logger.LogInformation("Creating new scope");

		_currentScope?.Dispose();
		_currentScope = RootServiceProvider.CreateScope();

		// Scope initializers run first and regardless of the installation; the others need a Skyrim directory.
		_logger.LogInformation("Running scope initializers");
		foreach (var initializer in ServiceProvider.GetServices<IScopeInitializer>())
		{
			initializer.Initialize();
		}

		if (string.IsNullOrEmpty(ServiceProvider.GetRequiredService<IOptionsSnapshot<AppSettings>>().Value.SkyrimDirectory))
		{
			return;
		}

		_logger.LogInformation("Running early configured scope initializers");
		foreach (var initializer in ServiceProvider.GetServices<IEarlyConfiguredScopeInitializer>())
		{
			initializer.Initialize();
		}

		_logger.LogInformation("Running configured scope initializers");
		foreach (var initializer in ServiceProvider.GetServices<IConfiguredScopeInitializer>())
		{
			initializer.Initialize();
		}
	}

	public void Dispose()
	{
		_logger.LogInformation("Disposing services");

		try
		{
			_currentScope?.Dispose();
		}
		finally
		{
			(RootServiceProvider as IDisposable)?.Dispose();
		}
	}

	private void RunGlobalInitializers()
	{
		_logger.LogInformation("Running early global initializers");
		foreach (var globalInitializer in RootServiceProvider.GetServices<IEarlyGlobalInitializer>())
		{
			globalInitializer.Initialize();
		}

		_logger.LogInformation("Running global initializers");
		foreach (var globalInitializer in RootServiceProvider.GetServices<IGlobalInitializer>())
		{
			globalInitializer.Initialize();
		}
	}
}
