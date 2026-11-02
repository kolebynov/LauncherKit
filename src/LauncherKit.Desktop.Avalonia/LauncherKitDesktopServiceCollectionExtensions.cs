using Microsoft.Extensions.Configuration;
using LauncherKit.Desktop.Avalonia.Extensions;
using LauncherKit.Desktop.Avalonia.Initialization;
using LauncherKit.Desktop.Avalonia.Interop;
using LauncherKit.Desktop.Avalonia.Services;
using LauncherKit.Desktop.Extensions;
using LauncherKit.Desktop.Localization;
using LauncherKit.Desktop.Services;
using LauncherKit.Desktop.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using LauncherKit.Core.Extensions;
using LauncherKit.Core.OAuth;
using LauncherKit.Core.Services.Abstractions;

namespace LauncherKit.Desktop.Avalonia;

public static class LauncherKitDesktopServiceCollectionExtensions
{
	/// <summary>Registers kit desktop behavior. The consumer supplies LauncherKitDesktopOptions and localization overrides.</summary>
	public static IServiceCollection AddLauncherKitDesktop(this IServiceCollection services)
	{
		services.AddEarlyGlobalInitializer(sp =>
			LanguageCulture.Apply(sp.GetRequiredService<IConfiguration>()["App:Language"] ?? string.Empty));
		services.TryAddSingleton<IUiDispatcher, AvaloniaUiDispatcher>();
		services.TryAddSingleton<FeedLoadStatusViewModel>();
		services.AddOptions<LoopbackBrowserOptions>().Configure<IStringLocalizer<DesktopStrings>>((options, strings) =>
		{
			options.Title = strings[DesktopStrings.OAuthTitle];
			options.Message = strings[DesktopStrings.OAuthMessage];
		});
		services.TryAddScoped<FirstSetupViewModel>();
		services.TryAddScoped<AppTaskQueueViewModel>();
		services.TryAddScoped<HomeViewModel>();
		services.TryAddScoped<DownloadsViewModel>();
		services.TryAddScoped<GraphPacksViewModel>();
		services.TryAddScoped<SettingsViewModel>(sp => new SettingsViewModel(
			sp.GetRequiredService<ISettingsService>(), sp.GetRequiredService<IUiDispatcher>(),
			string.IsNullOrWhiteSpace(sp.GetRequiredService<IConfiguration>()["Discord:ClientId"])
				? null : sp.GetRequiredService<DiscordIntegrationViewModel>()));
		services.TryAddScoped<ModpackChangelogViewModel>();
		services.TryAddScoped<OptionalModsViewModel>();
		services.TryAddTransient<ActivateBetaCodeViewModel>();
		services.TryAddScoped<DiscordIntegrationViewModel>();
		services.TryAddScoped<MainWindowViewModel>();
		services.TryAddScoped<IMainViewModelFactory, MainViewModelFactory>();
		services.TryAddScoped<WindowStateService>();
		services.AddScoped<IAppCloseHandler, PendingTasksCloseHandler>();
		services.AddScoped<IAppCloseHandler, SaveWindowStateCloseHandler>();
		services.TryAddSingleton(sp => new AppCloseCoordinator(
			sp.GetRequiredService<LauncherKitDesktopOptions>().GetCloseHandlers,
			sp.GetRequiredService<ILogger<AppCloseCoordinator>>()));
		services.AddConfiguredScopeInitializer<TaskbarProgressInitializer>();
		services.TryAddSingleton<Core.Services.Abstractions.IApplicationLifetime>(sp =>
			new ApplicationLifetime(sp.GetRequiredService<LauncherKitDesktopOptions>().GetLifetime()));
		services.TryAddSingleton<IExternalBrowser>(sp => new ExternalBrowser(() =>
			sp.GetRequiredService<LauncherKitDesktopOptions>().GetMainWindow()
				?? throw new InvalidOperationException("The consumer main window is not available.")));
		services.TryAddSingleton<ITaskbarProgressSink>(sp => new TaskbarProgressSink(
			sp.GetRequiredService<LauncherKitDesktopOptions>().GetMainWindow, TaskbarList.Create,
			sp.GetRequiredService<ILogger<TaskbarProgressSink>>()));
		services.TryAddSingleton<LauncherKitScopeReloadService>();
		services.TryAddSingleton<IAppReloader>(sp => new AppReloader(
			sp.GetRequiredService<LauncherKitScopeReloadService>().RequestConfigurationReload,
			() => sp.GetRequiredService<IUiDispatcher>().Post(() =>
			{
				LanguageCulture.ApplyToCurrentThread();
				sp.GetRequiredService<LauncherKitDesktopOptions>().ReloadViews();
			})));
		return services;
	}
}
