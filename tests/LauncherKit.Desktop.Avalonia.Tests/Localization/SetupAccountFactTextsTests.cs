using System.Globalization;
using LauncherKit.Desktop.Avalonia.Converters;
using LauncherKit.Desktop.Avalonia.Localization;
using LauncherKit.Desktop.Localization;
using LauncherKit.Desktop.ViewModels;
using LauncherKit.Desktop.ViewModels.Setup;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using LauncherKit.Core.Feed;

namespace LauncherKit.Desktop.Avalonia.Tests.Localization;

[TestFixture]
[NonParallelizable]
public sealed class SetupAccountFactTextsTests
{
	[TestCase("en")]
	[TestCase("ru")]
	public void EverySetupAccountFact_ResolvesResourceAndFormatsSetupArguments(string language)
	{
		var previous = CultureInfo.CurrentUICulture;
		try
		{
			CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language);
			using var provider = new ServiceCollection().AddLogging().AddLauncherKitLocalization().BuildServiceProvider();
			var texts = new SetupAccountFactTexts(provider.GetRequiredService<IStringLocalizer<DesktopStrings>>());
			foreach (var error in Enum.GetValues<DirectoryValidationError>()) AssertResolved(texts.Get(error));
			foreach (var membership in Enum.GetValues<DiscordMembership>()) AssertResolved(texts.Get(membership));
			ActivateBetaCodeResult[] results =
			[
				new ActivateBetaCodeResult.Success(), new ActivateBetaCodeResult.AlreadyActivated(),
				new ActivateBetaCodeResult.InvalidBetaCode(),
			];
			foreach (var result in results) AssertResolved(texts.Get(result));
			var setup = new SetupError(new Version(0, 1, 2), new Version(3, 4, 5));
			var message = texts.Get(setup);
			AssertResolved(message);
			Assert.That(message.Value, Does.Contain("0.1.2").And.Contain("3.4.5"));
		}
		finally
		{
			CultureInfo.CurrentUICulture = previous;
		}
	}

	[Test]
	public void Converter_BoxedNativeUnionAndTypedErrorCollection_MapsFacts()
	{
		var converter = SetupAccountFactConverter.Instance;
		object result = (ActivateBetaCodeResult)new ActivateBetaCodeResult.InvalidBetaCode();
		Assert.That(converter.Convert(result, typeof(string), null, CultureInfo.InvariantCulture),
			Is.EqualTo(LocalizationMarkup.SetupAccountFactTexts.Get((ActivateBetaCodeResult)result).Value));
		var errors = new object[] { DirectoryValidationError.InvalidPath, DirectoryValidationError.SkyrimDirectoryConflict };
		Assert.That(converter.Convert(errors, typeof(object), null, CultureInfo.InvariantCulture),
			Is.EqualTo(errors.Cast<DirectoryValidationError>().Select(error => LocalizationMarkup.SetupAccountFactTexts.Get(error).Value)));
	}

	private static void AssertResolved(LocalizedString text)
	{
		Assert.That(text.ResourceNotFound, Is.False, text.Name);
		Assert.That(text.Value, Is.Not.Empty.And.Not.EqualTo(text.Name));
	}
}
