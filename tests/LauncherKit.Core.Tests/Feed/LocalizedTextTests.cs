using System.Globalization;
using LauncherKit.Core.Feed;

namespace LauncherKit.Core.Tests.Feed;

[TestFixture]
public sealed class LocalizedTextTests
{
	[TestCase("en-US", "Regional")]
	[TestCase("en-GB", "English")]
	[TestCase("fr-CA", "Fallback")]
	[TestCase("", "Fallback")]
	public void Resolve_CultureHierarchy_UsesClosestTranslationOrFeedDefault(string culture, string expected)
	{
		// Arrange
		var sut = new LocalizedText { ["en-US"] = "Regional", ["en"] = "English", ["ru"] = "Fallback" };

		// Act
		var result = sut.Resolve(CultureInfo.GetCultureInfo(culture), "ru");

		// Assert
		Assert.That(result, Is.EqualTo(expected));
	}
}
