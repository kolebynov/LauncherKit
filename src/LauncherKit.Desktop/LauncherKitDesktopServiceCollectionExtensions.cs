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
using LauncherKit.Desktop.SingleInstance;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

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
		services.TryAddScoped<ModUpdateNotifications>();
		services.TryAddScoped<ILauncherMessageProcessor, LauncherMessageProcessor>();
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

	/// <summary>
	/// Has the scoped <typeparamref name="THandler"/> handle the messages of <typeparamref name="TMessage"/>'s
	/// <see cref="ILauncherMessage.Kind"/>, deserialized with the consumer's source-generated <paramref name="typeInfo"/>. Each
	/// kind has one handler; relaunches send them through <see cref="ILauncherMessageSender"/>; messages of unregistered kinds
	/// are logged and dropped.
	/// </summary>
	public static IServiceCollection AddLauncherMessageHandler<TMessage,
		[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] THandler>(this IServiceCollection services,
		JsonTypeInfo<TMessage> typeInfo) where TMessage : ILauncherMessage where THandler : class, ILauncherMessageHandler<TMessage>
	{
		var kind = TMessage.Kind;
		if (string.IsNullOrWhiteSpace(kind))
			throw new ArgumentException($"The launcher message kind of {typeof(TMessage).Name} is blank.", nameof(TMessage));
		ArgumentNullException.ThrowIfNull(typeInfo);
		if (services.Any(d => !d.IsKeyedService && d.ServiceType == typeof(LauncherMessageRegistration)
			&& d.ImplementationInstance is LauncherMessageRegistration registration && registration.Kind == kind))
			throw new ArgumentException($"A launcher message handler for the kind '{kind}' is already registered.", nameof(TMessage));
		services.TryAddScoped<THandler>();
		services.AddSingleton(new LauncherMessageRegistration(kind, (sp, payload) => sp.GetRequiredService<THandler>()
			.Handle(JsonSerializer.Deserialize(payload, typeInfo)
				?? throw new JsonException($"The '{kind}' launcher message is null."))));
		return services;
	}
}
