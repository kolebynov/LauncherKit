using Microsoft.Extensions.Configuration;
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

namespace LauncherKit.Desktop;

public static class LauncherKitDesktopServiceCollectionExtensions
{
	/// <summary>
	/// Registers kit desktop behavior. The consumer supplies LauncherKitDesktopOptions, its <see cref="IViewRegistry"/>
	/// and localization overrides.
	/// </summary>
	public static IServiceCollection AddLauncherKitDesktop(this IServiceCollection services)
	{
		services.AddEarlyGlobalInitializer(sp =>
			LanguageCulture.Apply(sp.GetRequiredService<IConfiguration>()["App:Language"] ?? string.Empty));
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
		services.AddSingleton<IAppCloseHandler, NavigationGuardCloseHandler>();
		services.AddScoped<IAppCloseHandler, PendingTasksCloseHandler>();
		services.TryAddSingleton<MainWindowStateHandler>();
		services.AddSingleton<IAppCloseHandler>(sp => sp.GetRequiredService<MainWindowStateHandler>());
		services.TryAddSingleton<MainWindowCloseHandler>();
		services.AddConfiguredScopeInitializer<TaskbarProgressInitializer>();
		services.AddGlobalInitializer<FeedLoadNotificationsInitializer>();
		services.TryAddSingleton<Core.Services.Abstractions.IApplicationLifetime>(sp =>
			new ApplicationLifetime(sp.GetRequiredService<LauncherKitDesktopOptions>().GetLifetime()));
		services.TryAddSingleton<IExternalBrowser>(sp => new ExternalBrowser(() =>
			sp.GetRequiredService<LauncherKitDesktopOptions>().GetMainWindow()
				?? throw new InvalidOperationException("The consumer main window is not available.")));
		services.TryAddSingleton<ITaskbarProgressSink>(sp => new TaskbarProgressSink(
			sp.GetRequiredService<LauncherKitDesktopOptions>().GetMainWindow, TaskbarList.Create,
			sp.GetRequiredService<ILogger<TaskbarProgressSink>>()));
		services.TryAddSingleton<INotificationService>(sp => new NotificationService(
			sp.GetRequiredService<IServiceProviderIsService>().IsService(typeof(INotificationManagerProvider))
				? () => sp.GetRequiredService<INotificationManagerProvider>().NotificationManager
				: null));
		services.TryAddSingleton<LauncherKitScopeReloadService>();
		services.TryAddSingleton<IAppReloader>(sp => new AppReloader(
			() => sp.GetRequiredService<IUiDispatcher>().Post(() =>
			{
				LanguageCulture.ApplyToCurrentThread();
				sp.GetRequiredService<LauncherKitDesktopOptions>().ReloadViews();
			})));
		services.TryAddSingleton(sp => new ViewLocator(
			sp.GetService<IViewRegistry>() ?? throw new InvalidOperationException(
				$"No {nameof(IViewRegistry)} is registered: the consumer must register its view model to view mapping."),
			sp.GetRequiredService<LauncherKitDesktopOptions>().GetCurrentServices));
		return services;
	}
}
