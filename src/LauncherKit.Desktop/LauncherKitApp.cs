using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using LauncherKit.Core;
using LauncherKit.Core.Extensions;
using LauncherKit.Desktop.Extensions;
using LauncherKit.Desktop.Localization;
using LauncherKit.Desktop.ViewModels;
using LauncherKit.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using LauncherKit.Desktop.SingleInstance;
using R3;
using Serilog;
using Serilog.Events;
using AppBuilder = LauncherKit.Core.AppBuilder;

namespace LauncherKit.Desktop;

/// <summary>
/// The kit's Avalonia application: composes logging, configuration, kit services and the application scope, has the
/// consumer load its XAML, installs the view locator, shows the main window with the current scope's
/// <see cref="MainWindowViewModelBase"/> and disposes everything on exit. A consumer derives its <c>App</c> from it and only
/// supplies its registrations, views and main window through the abstract and virtual members; its identity comes from
/// <see cref="LauncherKitProgram"/>.
/// </summary>
public abstract class LauncherKitApp : Application
{
	private AppInstance? _appInstance;
	private ILogger<LauncherKitApp> _logger = null!;

	/// <summary>The consumer's complete product identity, given to <see cref="LauncherKitProgram"/>.</summary>
	internal LauncherProductOptions Product { get; init; } = null!;

	/// <summary>
	/// The owner's launch requests (its own launch first), created before the services so requests admitted during startup
	/// wait; the services
	/// register it as their singleton; without it (designer) they register an empty one.
	/// </summary>
	internal LaunchRequestQueue? OwnerLaunchRequests { get; init; }

	internal AppInstance AppInstance => _appInstance ?? throw new InvalidOperationException("The application is not initialized.");

	/// <summary>The current application scope's services; the scope is replaced when installation settings change.</summary>
	public IServiceProvider Services => AppInstance.ServiceProvider;

	public sealed override void Initialize()
	{
		LoadXaml();
	}

	public sealed override void OnFrameworkInitializationCompleted()
	{
		base.OnFrameworkInitializationCompleted();

		var appBuilder = new AppBuilder(Product);
		ConfigureAppServices(appBuilder, ApplicationLifetime as IClassicDesktopStyleApplicationLifetime);
		_appInstance = appBuilder.Build();

		_logger = _appInstance.RootServiceProvider.GetRequiredService<ILogger<LauncherKitApp>>();

		AppDomain.CurrentDomain.UnhandledException += (_, args) =>
		{
			_logger.LogError(args.ExceptionObject as Exception, "Unhandled exception");
			if (args.IsTerminating) Log.CloseAndFlush();
		};
		TaskScheduler.UnobservedTaskException += (_, args) =>
		{
			_logger.LogError(args.Exception, "Unobserved task exception");
			if (args.Exception.InnerException is OperationCanceledException) args.SetObserved();
		};

		ObservableSystem.RegisterUnhandledExceptionHandler(e => _logger.LogError(e, "Unhandled observable system exception"));

		try
		{
			LanguageCulture.Apply(_appInstance!.Configuration["App:Language"]);
			if (Design.IsDesignMode) _appInstance.InitializeDesignMode();
			else _appInstance.Initialize();

			DataTemplates.Insert(0, Services.GetRequiredService<ViewLocator>());
			LocalizationMarkup.Configure(Services.GetRequiredService<IStringLocalizer<DesktopStrings>>());
			OnServicesBuilt();

			if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
			{
				var mainView = CreateMainView();
				var mainWindow = mainView.AsWindow();
				// Global initializers set the window up (state, navigation guard, close handling) when it is assigned, so
				// it is published before its data context.
				((AvaloniaApplicationLifetime)Services.GetRequiredService<IAvaloniaApplicationLifetime>()).MainWindow = mainView;
				mainWindow.DataContext = Services.GetRequiredService<MainWindowViewModelBase>();
				desktop.MainWindow = mainWindow;
				var desktopServices = StartDesktopServices(desktop);
				desktop.Exit += (_, _) =>
				{
					_logger.LogInformation("Exiting...");
					try
					{
						desktopServices?.Dispose();
						_appInstance.Dispose();
					}
					finally
					{
						Log.CloseAndFlush();
					}
				};
			}

			_logger.LogInformation("OnFrameworkInitializationCompleted finished");
		}
		catch (Exception e1)
		{
			_logger.LogError(e1, "Failed to initialize");
			throw;
		}
	}

	/// <summary>
	/// Loads the application XAML; implement it as <c>AvaloniaXamlLoader.Load(this)</c>. The call must be written in the
	/// consumer's <c>x:Class</c> type: Avalonia's XAML compiler binds the precompiled XAML only to calls in that type's own
	/// code, and otherwise populates the XAML from the constructor, before the application is set up. Called from
	/// <see cref="Initialize"/>, before the services are built.
	/// </summary>
	protected abstract void LoadXaml();

	/// <summary>
	/// Runs after logging is configured and before the configuration files are read, e.g. to migrate files the
	/// configuration reads. Consumers own any dependencies needed by this pre-DI operation.
	/// </summary>
	protected virtual void BeforeConfiguration(LauncherProductOptions product)
	{
	}

	/// <summary>
	/// Registers the consumer's services (views, main window, view-side services, initializers, localization of its views)
	/// after the kit's configuration and desktop services; <paramref name="builder"/>'s configuration is already loaded.
	/// </summary>
	protected abstract void ConfigureServices(AppBuilder builder);

	/// <summary>
	/// Runs after the services and the current scope are created and the kit's localization is configured, before the main
	/// window is created; also runs in the designer.
	/// </summary>
	protected virtual void OnServicesBuilt()
	{
	}

	/// <summary>
	/// Starts optional services that need the desktop lifetime and main window (e.g. development automation); the result
	/// is disposed on exit, before the application services.
	/// </summary>
	protected virtual IDisposable? StartDesktopServices(IClassicDesktopStyleApplicationLifetime desktop) => null;

	/// <summary>
	/// Configuration values compiled into the consumer rather than shipped in <c>appsettings.json</c> (e.g. server bearer
	/// tokens). They are added first, so the configuration files and environment variables override them.
	/// </summary>
	protected virtual IEnumerable<KeyValuePair<string, string?>> GetInMemoryConfiguration() => [];

	protected abstract IMainView CreateMainView();

	private void ConfigureAppServices(AppBuilder appBuilder, IClassicDesktopStyleApplicationLifetime? applicationLifetime)
	{
		var services = appBuilder.Services;
		ConfigureLogging(services);
		BeforeConfiguration(appBuilder.Product);

		appBuilder.Configuration.AddInMemoryCollection(GetInMemoryConfiguration());
		appBuilder.Configuration.AddLauncherKitConfiguration(Directory.GetCurrentDirectory(), appBuilder.Product);
		services.AddLauncherKitLocalization(GetType().Assembly);
		// The owner's queue outlives the services: the program disposes it only after its listener has stopped, so both
		// registrations are of the instance, which the services never dispose. Without an owner (designer) the services
		// create and dispose an empty one.
		if (OwnerLaunchRequests is { } ownerLaunchRequests)
		{
			services.AddSingleton(ownerLaunchRequests);
			services.AddSingleton<ILaunchRequestQueue>(ownerLaunchRequests);
		}
		else
		{
			services.AddSingleton<LaunchRequestQueue>();
			services.AddSingleton<ILaunchRequestQueue>(sp => sp.GetRequiredService<LaunchRequestQueue>());
		}

		services.AddScopeInitializer<LaunchRequestPump>();

		services.AddLauncherKitDesktop(applicationLifetime);
		ConfigureServices(appBuilder);
#if RELEASE
		services.AddInstallationAnalytics();
#endif
	}

	private static void ConfigureLogging(IServiceCollection services)
	{
		Log.Logger = new LoggerConfiguration()
			.Enrich.FromLogContext()
			.MinimumLevel.Information()
			.MinimumLevel.Override("Polly", LogEventLevel.Warning)
			.MinimumLevel.Override($"System.Net.Http.HttpClient.{AppConstants.S3HttpClient}.ClientHandler", LogEventLevel.Warning)
			.MinimumLevel.Override($"System.Net.Http.HttpClient.{AppConstants.S3DownloadHttpClient}.ClientHandler", LogEventLevel.Warning)
			.MinimumLevel.Override($"System.Net.Http.HttpClient.{AppConstants.HttpFileSourceHttpClient}.ClientHandler", LogEventLevel.Warning)
			.MinimumLevel.Override($"System.Net.Http.HttpClient.{AppConstants.AnalyticsHttpClient}.ClientHandler", LogEventLevel.Warning)
			.WriteTo.File(
				"logs/log-.log",
				outputTemplate: "{Timestamp:yyyy.MM.dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}",
				rollingInterval: RollingInterval.Day,
				retainedFileCountLimit: 30,
				fileSizeLimitBytes: 33554432,
				rollOnFileSizeLimit: true,
				formatProvider: CultureInfo.InvariantCulture)
			.CreateLogger();
		services.AddLogging(builder => builder.AddSerilog(dispose: false));
	}
}
