using System.Globalization;
using LauncherKit.Desktop.Localization;
using LauncherKit.Desktop.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;

namespace LauncherKit.Desktop.Tests.Localization;

[TestFixture]
[NonParallelizable]
public sealed class HomeCardFactTextsTests
{
	[TestCase("en")]
	[TestCase("ru")]
	public void EveryUnavailableFact_ResolvesResourceAndFormatsRawArguments(string language)
	{
		var previous = CultureInfo.CurrentUICulture;
		try
		{
			CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language);
			using var provider = new ServiceCollection().AddLauncherKitLocalization().BuildServiceProvider();
			var texts = provider.GetRequiredService<HomeCardFactTexts>();
			AssertResolved(texts.Get(ModpackInstallUnavailableReason.NoAvailableVersions));
			AssertResolved(texts.Get(new ModCardViewModel.InstallUnavailableReason.Updating()));
			AssertResolved(texts.Get(new ModCardViewModel.InstallUnavailableReason.ModpackTaskQueued()));
			var conflict = texts.Get(new ModCardViewModel.InstallUnavailableReason.OtherGraphPackInstalled("Example pack"));
			AssertResolved(conflict);
			Assert.That(conflict.Value, Does.Contain("Example pack"));
		}
		finally
		{
			CultureInfo.CurrentUICulture = previous;
		}
	}

	private static void AssertResolved(LocalizedString text)
	{
		Assert.That(text.ResourceNotFound, Is.False, text.Name);
		Assert.That(text.Value, Is.Not.Empty.And.Not.EqualTo(text.Name));
	}
}
