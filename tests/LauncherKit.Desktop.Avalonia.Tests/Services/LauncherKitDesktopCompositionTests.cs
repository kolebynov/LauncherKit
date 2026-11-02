using Microsoft.Extensions.DependencyInjection;
using LauncherKit.Desktop.Avalonia.Extensions;
using LauncherKit.Desktop.Avalonia.Services;
using LauncherKit.Desktop.Extensions;
using LauncherKit.Desktop.Services;
using LauncherKit.Desktop.ViewModels;
using LauncherKit.Core.Initialization;

namespace LauncherKit.Desktop.Avalonia.Tests.Services;

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
				typeof(DownloadsViewModel), typeof(GraphPacksViewModel), typeof(OptionalModsViewModel),
				typeof(SettingsViewModel), typeof(ModpackChangelogViewModel),
			})
				Assert.That(services.Single(x => x.ServiceType == model).Lifetime, Is.EqualTo(ServiceLifetime.Scoped));
			Assert.That(services.Single(x => x.ServiceType == typeof(IUiDispatcher)).Lifetime, Is.EqualTo(ServiceLifetime.Singleton));
			Assert.That(services.Single(x => x.ServiceType == typeof(LauncherKitScopeReloadService)).Lifetime, Is.EqualTo(ServiceLifetime.Singleton));
			Assert.That(services.Count(x => x.ServiceType == typeof(IAppCloseHandler)), Is.EqualTo(2));
			Assert.That(services.Any(x => x.ServiceType == typeof(IConfiguredScopeInitializer)), Is.True);
			Assert.That(services.Any(x => x.ServiceType.Name == "INotificationService"), Is.False);
			Assert.That(services.Any(x => x.ServiceType.Name == "IDialogService"), Is.False);
		});
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
		GetCloseHandlers = () => [],
		RefreshMainWindow = () => { },
		ReloadViews = () => { },
	};
}
