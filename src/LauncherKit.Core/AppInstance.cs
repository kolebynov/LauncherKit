using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using LauncherKit.Core.Initialization;
using LauncherKit.Infrastructure;

namespace LauncherKit.Core;

/// <summary>The built service provider and its replaceable application scope; owned by the kit's application host.</summary>
internal sealed class AppInstance : IDisposable
{
	private readonly IServiceProvider _rootServiceProvider;
	private readonly ILogger<AppInstance> _logger;
	private AsyncServiceScope? _currentScope;

	public IServiceProvider ServiceProvider => _currentScope!.Value.ServiceProvider;

	internal AppInstance(IServiceProvider rootServiceProvider)
	{
		_rootServiceProvider = rootServiceProvider;
		_logger = rootServiceProvider.GetRequiredService<ILogger<AppInstance>>();

		Initialize();
	}

	public void CreateNewScope()
	{
		_logger.LogInformation("Creating new scope");

		if (_currentScope is { } previousScope)
		{
			// Start disposal outside the caller's synchronization context, not merely its final wait.
			// A scoped async disposable may otherwise capture the UI thread that this synchronous API blocks.
			Task.Run(async () => await previousScope.DisposeAsync()).GetAwaiter().GetResult();
		}

		_currentScope = _rootServiceProvider.CreateAsyncScope();

		if (string.IsNullOrEmpty(ServiceProvider.GetRequiredService<IOptionsSnapshot<AppSettings>>().Value.SkyrimDirectory))
		{
			return;
		}

		_logger.LogInformation("Running early scope initializers");
		Task.Run(async () =>
		{
			foreach (var initializer in ServiceProvider.GetServices<IEarlyConfiguredScopeInitializer>())
			{
				await initializer.Initialize();
			}
		}).GetAwaiter().GetResult();

		_logger.LogInformation("Running scope initializers");
		Task.Run(async () =>
		{
			foreach (var initializer in ServiceProvider.GetServices<IConfiguredScopeInitializer>())
			{
				await initializer.Initialize();
			}
		}).GetAwaiter().GetResult();
	}

	public void Dispose()
	{
		_logger.LogInformation("Disposing services");

		try
		{
			Task.Run(() => DisposeServicesImpl().WaitAsync(TimeSpan.FromSeconds(5))).GetAwaiter().GetResult();
		}
		catch (TimeoutException)
		{
			_logger.LogWarning("Services disposal did not finish within 5 seconds");
		}
		catch (Exception e)
		{
			Console.WriteLine($"Error during final services disposal: {e}");
		}

		_logger.LogInformation("Waiting for 1 second before exiting...");

		Thread.Sleep(1000);

		async Task DisposeServicesImpl()
		{
			await (_currentScope?.DisposeAsync() ?? ValueTask.CompletedTask);
			var rootDisposeTask = (_rootServiceProvider as IAsyncDisposable)?.DisposeAsync();
			if (rootDisposeTask != null)
			{
				await rootDisposeTask.Value;
			}
			else
			{
				(_rootServiceProvider as IDisposable)?.Dispose();
			}
		}
	}

	private void Initialize()
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

	private void RunGlobalInitializers()
	{
		_logger.LogInformation("Running early global initializers");
		Task.Run(async () =>
		{
			foreach (var globalInitializer in _rootServiceProvider.GetServices<IEarlyGlobalInitializer>())
			{
				await globalInitializer.Initialize();
			}
		}).GetAwaiter().GetResult();

		_logger.LogInformation("Running global initializers");
		Task.Run(async () =>
		{
			foreach (var globalInitializer in _rootServiceProvider.GetServices<IGlobalInitializer>())
			{
				await globalInitializer.Initialize();
			}
		}).GetAwaiter().GetResult();
	}
}