using LauncherKit.Desktop.Localization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;

namespace LauncherKit.Desktop.Tests;

/// <summary>Configures XAML localization once, as <see cref="LauncherKitApp"/> does before loading views.</summary>
[SetUpFixture]
public sealed class LocalizationSetUp
{
	[OneTimeSetUp]
	public void ConfigureLocalizationMarkup()
	{
		using var services = new ServiceCollection().AddLauncherKitLocalization().BuildServiceProvider();
		LocalizationMarkup.Configure(services.GetRequiredService<IStringLocalizer<DesktopStrings>>());
	}
}
