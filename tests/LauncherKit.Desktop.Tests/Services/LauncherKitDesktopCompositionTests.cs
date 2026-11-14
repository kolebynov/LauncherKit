using Microsoft.Extensions.DependencyInjection;
using LauncherKit.Desktop.Services;
using LauncherKit.Desktop.ViewModels;
using LauncherKit.Desktop.ViewModels.Setup;
using LauncherKit.Core.Initialization;

namespace LauncherKit.Desktop.Tests.Services;

[TestFixture]
public sealed class LauncherKitDesktopCompositionTests
{
	[Test]
	public void DesktopRegistration_KeepsPageModelsScopedAndPlatformServicesSingleton()
	{
		var services = new ServiceCollection();
		services.AddLauncherKitDesktop();

		Assert.Multiple(() =>
		{
			foreach (var model in new[]
			{
				typeof(FirstSetupViewModel), typeof(AppTaskQueueViewModel), typeof(HomeViewModel),
				typeof(DownloadsViewModel), typeof(ModCategoriesViewModel),
				typeof(SettingsViewModel), typeof(ModpackChangelogViewModel),
			})
				Assert.That(services.Single(x => x.ServiceType == model).Lifetime, Is.EqualTo(ServiceLifetime.Scoped));
			Assert.That(services.Single(x => x.ServiceType == typeof(IUiDispatcher)).Lifetime, Is.EqualTo(ServiceLifetime.Singleton));
			Assert.That(services.Single(x => x.ServiceType == typeof(LauncherKitScopeReloadService)).Lifetime, Is.EqualTo(ServiceLifetime.Singleton));
			Assert.That(services.Count(x => x.ServiceType == typeof(IAppCloseHandler)), Is.EqualTo(3));
			Assert.That(services.Any(x => x.ServiceType == typeof(IConfiguredScopeInitializer)), Is.True);
			Assert.That(services.Single(x => x.ServiceType == typeof(INotificationService)).Lifetime, Is.EqualTo(ServiceLifetime.Singleton));
			Assert.That(services.Any(x => x.ServiceType == typeof(IDialogService)), Is.False);
		});
	}

	[Test]
	public void NotificationService_ManagerProviderRegistered_DoesNotResolveProviderOnCreation()
	{
		// Arrange
		var services = new ServiceCollection();
		services.AddLauncherKitDesktop();
		var providerResolved = false;
		services.AddSingleton<INotificationManagerProvider>(_ =>
		{
			providerResolved = true;
			return null!;
		});
		using var provider = services.BuildServiceProvider();

		// Act
		provider.GetRequiredService<INotificationService>();

		// Assert
		Assert.That(providerResolved, Is.False);
	}

	[Test]
	public void ViewLocator_NoViewRegistryRegistered_ThrowsNamingIt()
	{
		// Arrange
		var services = new ServiceCollection();
		services.AddLauncherKitDesktop();
		services.AddSingleton(CreateOptions(() => throw new AssertionException("No scope expected.")));
		using var provider = services.BuildServiceProvider();

		// Act & Assert
		Assert.That(() => provider.GetRequiredService<ViewLocator>(),
			Throws.InvalidOperationException.With.Message.Contains(nameof(IViewRegistry)));
	}

	private static LauncherKitDesktopOptions CreateOptions(Func<IServiceProvider> currentServices) => new()
	{
		GetMainWindow = () => null,
		GetLifetime = () => throw new AssertionException("No lifetime expected."),
		GetCurrentServices = currentServices,
		RefreshMainWindow = () => { },
		ReloadViews = () => { },
	};
}
