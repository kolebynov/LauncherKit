using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Styling;
using FluentAvalonia.UI.Controls;
using LauncherKit.Core;
using LauncherKit.Core.Extensions;
using LauncherKit.Desktop.Avalonia.Localization;
using LauncherKit.Desktop.Avalonia.Services;
using LauncherKit.Desktop.Localization;
using LauncherKit.Desktop.Services;
using LauncherKit.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using R3;
using Serilog;
using Serilog.Events;
using AppBuilder = LauncherKit.Core.AppBuilder;

namespace LauncherKit.Desktop.Avalonia;

/// <summary>
/// The kit's Avalonia application: composes logging, configuration, kit services and the application scope, has the
/// consumer load its XAML, installs the view locator, shows the main window and disposes everything on exit. A
/// consumer derives its <c>App</c> from it and only supplies its identity, registrations, views and shell through the
/// abstract and virtual members.
/// </summary>
public abstract class LauncherKitApp : Application
{
	private AppInstance? _appInstance;
	private ILogger<LauncherKitApp> _logger = null!;

	/// <summary>The current application scope's services; the scope is replaced when installation settings change.</summary>
	public IServiceProvider Services => (_appInstance ?? throw new InvalidOperationException("The application is not initialized.")).ServiceProvider;

	public sealed override void Initialize()
	{
		var appBuilder = new AppBuilder(CreateProductOptions());
		ConfigureAppServices(appBuilder);
		_appInstance = appBuilder.Build();
		LocalizationMarkup.Configure(Services.GetRequiredService<IStringLocalizer<DesktopStrings>>());
		OnServicesBuilt(Services);
		LoadXaml();
		_logger = Services.GetRequiredService<ILogger<LauncherKitApp>>();
		InstallViewLocator();
		Services.GetRequiredService<LauncherKitScopeReloadService>().Start(_appInstance.CreateNewScope);

		AppDomain.CurrentDomain.UnhandledException += (_, args) =>
		{
			_logger.LogError(args.ExceptionObject as Exception, "Unhandled exception");
			if (args.IsTerminating) DisposeServices();
		};
		TaskScheduler.UnobservedTaskException += (_, args) =>
		{
			_logger.LogError(args.Exception, "Unobserved task exception");
			if (args.Exception.InnerException is OperationCanceledException) args.SetObserved();
		};
	}

	public sealed override void OnFrameworkInitializationCompleted()
	{
		try
		{
			_logger.LogInformation("OnFrameworkInitializationCompleted started");
			base.OnFrameworkInitializationCompleted();
			if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop) return;
			ObservableSystem.RegisterUnhandledExceptionHandler(e => _logger.LogError(e, "Unhandled observable system exception"));
			var mainWindow = GetMainWindow(Services);
			mainWindow.DataContext = GetShellViewModel(Services);
			desktop.MainWindow = mainWindow;
			var desktopServices = StartDesktopServices(desktop);
			desktop.Exit += (_, _) =>
			{
				_logger.LogInformation("Exiting...");
				desktopServices?.Dispose();
				DisposeServices();
			};
			_logger.LogInformation("OnFrameworkInitializationCompleted finished");
		}
		catch (Exception e)
		{
			_logger.LogError(e, "Failed to initialize");
			throw;
		}
	}

	/// <summary>The consumer's complete product identity; validated before anything reads configuration or state.</summary>
	protected abstract LauncherProductOptions CreateProductOptions();

	/// <summary>
	/// Loads the application XAML; implement it as <c>AvaloniaXamlLoader.Load(this)</c>. The call must be written in the
	/// consumer's <c>x:Class</c> type: Avalonia's XAML compiler binds the precompiled XAML only to calls in that type's own
	/// code, and otherwise populates the XAML from the constructor, before the application is set up. Called after the
	/// services are built and <see cref="OnServicesBuilt"/>, so XAML markup can resolve localized texts.
	/// </summary>
	protected abstract void LoadXaml();

	/// <summary>
	/// Runs after logging is configured and before the configuration files are read, e.g. to migrate files the
	/// configuration reads. The logger factory is valid only during the call.
	/// </summary>
	protected virtual void BeforeConfiguration(LauncherProductOptions product, ILoggerFactory loggerFactory)
	{
	}

	/// <summary>
	/// Registers the consumer's services (views, shell, view-side services, initializers, localization of its views)
	/// after the kit's configuration and desktop services; <paramref name="builder"/>'s configuration is already loaded.
	/// </summary>
	protected abstract void ConfigureServices(AppBuilder builder);

	/// <summary>Runs once the services are built and the initial scope exists, before the application XAML is loaded.</summary>
	protected virtual void OnServicesBuilt(IServiceProvider services)
	{
	}

	/// <summary>The consumer's main window; called once at startup with the current scope's services.</summary>
	protected abstract Window GetMainWindow(IServiceProvider services);

	/// <summary>The consumer's shell view model, set as the main window's data context at startup and after scope reloads.</summary>
	protected abstract object GetShellViewModel(IServiceProvider services);

	/// <summary>
	/// Rebuilds the rendered pages after a language change, keeping their view models and the scope that owns the tasks;
	/// the consumer must also clear its navigation caches, which keep the old texts.
	/// </summary>
	protected abstract void ReloadViews(Window mainWindow);

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

	// The locator (needing the consumer's IViewRegistry registration) is the first application data template, so it
	// builds every view model's view, and every FAFrame builds its pages through it.
	private void InstallViewLocator()
	{
		DataTemplates.Insert(0, Services.GetRequiredService<ViewLocator>());
		Styles.Add(new Style(static selector => selector.OfType<FAFrame>())
		{
			Setters = { new Setter(FAFrame.NavigationPageFactoryProperty, Services.GetRequiredService<FramePageFactory>()) },
		});
	}

	private void ConfigureAppServices(AppBuilder appBuilder)
	{
		var services = appBuilder.Services;
		ConfigureLogging(services);
		using (var loggerFactory = new Serilog.Extensions.Logging.SerilogLoggerFactory(Log.Logger))
		{
			BeforeConfiguration(appBuilder.Product, loggerFactory);
		}

		appBuilder.Configuration.AddInMemoryCollection(GetInMemoryConfiguration());
		appBuilder.Configuration.AddLauncherKitConfiguration(Directory.GetCurrentDirectory(), appBuilder.Product);
		services.AddLauncherKitLocalization(GetType().Assembly);
		services.AddSingleton(new LauncherKitDesktopOptions
		{
			GetMainWindow = () => (ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow,
			GetLifetime = () => (IClassicDesktopStyleApplicationLifetime)ApplicationLifetime!,
			GetCurrentServices = () => Services,
			GetCloseHandlers = () => Services.GetServices<IAppCloseHandler>(),
			RefreshShell = () =>
			{
				if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow: { } window })
					window.DataContext = GetShellViewModel(Services);
			},
			ReloadViews = () =>
			{
				if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow: { } window })
					ReloadViews(window);
			},
		});
		services.AddLauncherKitDesktop();
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

	private void DisposeServices()
	{
		_appInstance?.Dispose();
		Log.CloseAndFlush();
	}
}
