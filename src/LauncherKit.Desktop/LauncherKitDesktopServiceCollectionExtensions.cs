using System.ComponentModel;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using LauncherKit.Core.Initialization;
using LauncherKit.Infrastructure;
using LauncherKit.Desktop.Initialization;
using LauncherKit.Desktop.Interop;
using LauncherKit.Desktop.Services;
using LauncherKit.Desktop.Localization;
using LauncherKit.Desktop.ViewModels;
using LauncherKit.Desktop.ViewModels.Setup;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using LauncherKit.Core.Extensions;
using LauncherKit.Core.OAuth;
using LauncherKit.Core.Services.Abstractions;
using LauncherKit.Desktop.SingleInstance;
using System.Diagnostics.CodeAnalysis;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using IApplicationLifetime = LauncherKit.Core.Services.Abstractions.IApplicationLifetime;

namespace LauncherKit.Desktop;

public static class LauncherKitDesktopServiceCollectionExtensions
{
	/// <summary>
	/// Registers kit desktop behavior. The consumer supplies LauncherKitDesktopOptions, its <see cref="IViewRegistry"/>
	/// and localization overrides.
	/// </summary>
	public static IServiceCollection AddLauncherKitDesktop(
		this IServiceCollection services, IClassicDesktopStyleApplicationLifetime? applicationLifetime)
	{
		services.TryAddSingleton<IUiDispatcher, AvaloniaUiDispatcher>();
		services.TryAddSingleton<IMarkdownLoader, MarkdownLoader>();
		services.TryAddSingleton<FeedLoadStatusViewModel>();
		services.TryAddSingleton<AvailabilityCheckState>();
		services.AddOptions<LoopbackBrowserOptions>().Configure<IStringLocalizer<DesktopStrings>>((options, strings) =>
		{
			options.Title = strings[DesktopStrings.OAuthTitle];
			options.Message = strings[DesktopStrings.OAuthMessage];
		});
		services.TryAddScoped(sp =>
		{
			var settingsService = sp.GetRequiredService<ISettingsService>();
			var context = new FirstSetupContext(settingsService.GetSettings());
			return new FirstSetupViewModel(FirstSetupSteps.Create(sp.GetServices<ISetupStepProvider>(), context), context,
				settingsService);
		});
		services.TryAddEnumerable(
		[
			ServiceDescriptor.Scoped<ISetupStepProvider, LanguageSetupStepProvider>(),
			ServiceDescriptor.Scoped<ISetupStepProvider, SkyrimDirectorySetupStepProvider>(),
			ServiceDescriptor.Scoped<ISetupStepProvider, AppDataDirectorySetupStepProvider>(),
			ServiceDescriptor.Scoped<ISetupStepProvider, AvailabilitySetupStepProvider>(),
		]);
		services.AddLaunchHandler<MainWindowActivationLaunchHandler>();
		services.TryAddScoped<AppTaskQueueViewModel>();
		services.TryAddScoped<HomeViewModel>();
		services.TryAddScoped<DownloadsViewModel>();
		services.TryAddScoped<ModCategoriesViewModel>();
		services.TryAddScoped<SettingsViewModel>(sp => new SettingsViewModel(
			sp.GetRequiredService<ISettingsService>(), sp.GetRequiredService<IUiDispatcher>(),
			string.IsNullOrWhiteSpace(sp.GetRequiredService<IConfiguration>()["Discord:ClientId"])
				? null : sp.GetRequiredService<DiscordIntegrationViewModel>()));
		services.TryAddScoped<ModpackChangelogViewModel>();
		services.TryAddTransient<ActivateBetaCodeViewModel>();
		services.TryAddScoped<DiscordIntegrationViewModel>();
		services.TryAddScoped<ModUpdateNotifications>();
		services.AddSingleton<IAppCloseHandler, NavigationGuardCloseHandler>();
		services.AddScoped<IAppCloseHandler, PendingTasksCloseHandler>();
		services.TryAddSingleton<MainWindowStateHandler>();
		services.AddSingleton<IAppCloseHandler>(sp => sp.GetRequiredService<MainWindowStateHandler>());
		// Run in registration order when the main window is assigned, before its data context is set.
		services.AddSingleton<IGlobalInitializer>(sp => sp.GetRequiredService<MainWindowStateHandler>());
		services.AddGlobalInitializer<NavigationGuard>();
		services.TryAddSingleton<MainWindowCloseHandler>();
		services.AddSingleton<IGlobalInitializer>(sp => sp.GetRequiredService<MainWindowCloseHandler>());
		// Once per process, only for the installation configured at startup: later scopes (first setup, Skyrim directory
		// changes) are not checked. Waiting for the window to open lets its notification manager apply its template first.
		services.AddGlobalInitializer(sp =>
		{
			if (string.IsNullOrEmpty(sp.GetRequiredService<IOptionsMonitor<AppSettings>>().CurrentValue.SkyrimDirectory))
			{
				return;
			}

			var lifetime = sp.GetRequiredService<IAvaloniaApplicationLifetime>();
			lifetime.MainWindowOpened.Register(() => _ = lifetime.Services.GetRequiredService<ModUpdateNotifications>().Start());
		});
		services.AddConfiguredScopeInitializer<TaskbarProgressInitializer>();
		services.AddGlobalInitializer<FeedLoadNotificationsInitializer>();
		if (applicationLifetime != null)
		{
			services.TryAddSingleton<IAvaloniaApplicationLifetime>(
				_ => new AvaloniaApplicationLifetime(applicationLifetime));
		}
		else
		{
			services.TryAddSingleton<IAvaloniaApplicationLifetime, DesignAvaloniaApplicationLifetime>();
		}

		services.TryAddSingleton<IApplicationLifetime>(sp => sp.GetRequiredService<IAvaloniaApplicationLifetime>());
		services.TryAddSingleton<IExternalBrowser, ExternalBrowser>();
		services.TryAddSingleton<ITaskbarProgressSink>(sp => new TaskbarProgressSink(
			sp.GetRequiredService<IAvaloniaApplicationLifetime>(), TaskbarList.Create,
			sp.GetRequiredService<ILogger<TaskbarProgressSink>>()));
		services.TryAddSingleton<INotificationService>(sp => new NotificationService(
			sp.GetRequiredService<IServiceProviderIsService>().IsService(typeof(INotificationManagerProvider))
				? () => sp.GetRequiredService<INotificationManagerProvider>().NotificationManager
				: null));
		services.AddGlobalInitializer<LauncherKitScopeReloadService>();
		services.TryAddSingleton<IAppReloader>(sp => new AppReloader(
			() => sp.GetRequiredService<IUiDispatcher>().Post(() =>
			{
				LanguageCulture.ApplyToCurrentThread();
				sp.GetRequiredService<IAvaloniaApplicationLifetime>().ReloadViews();
			})));
		services.TryAddSingleton(sp => new ViewLocator(
			sp.GetService<IViewRegistry>() ?? throw new InvalidOperationException(
				$"No {nameof(IViewRegistry)} is registered: the consumer must register its view model to view mapping."),
			sp.GetRequiredService<IAvaloniaApplicationLifetime>()));
		return services;
	}

	/// <summary>
	/// Adds the scoped <typeparamref name="THandler"/> to the launcher's <see cref="ILaunchHandler"/>s, which handle every
	/// launch (the one that started the launcher and each relaunch forwarded to it) in registration order. Adding the same
	/// type again does nothing. <see cref="AddLauncherKitDesktop"/> registers the kit's main window activation handler first,
	/// so relaunches bring the main window to the front before consumer handlers run.
	/// Without any handlers, launches are dropped.
	/// </summary>
	public static IServiceCollection AddLaunchHandler<
		[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] THandler>(this IServiceCollection services)
		where THandler : class, ILaunchHandler
	{
		services.TryAddEnumerable(ServiceDescriptor.Scoped<ILaunchHandler, THandler>());
		return services;
	}

	private sealed class DesignAvaloniaApplicationLifetime : IAvaloniaApplicationLifetime
	{
		public IServiceProvider Services => ((LauncherKitApp)Application.Current!).Services;

		public IMainView? MainWindow => null;

		public CancellationToken MainWindowOpened => CancellationToken.None;

		public event PropertyChangedEventHandler? PropertyChanged
		{
			add { }
			remove { }
		}

		public void ReloadViews()
		{
		}

		public void CreateNewScope()
		{
		}

		public void Exit()
		{
		}
	}
}
