using System.Globalization;
using LauncherKit.Desktop.Avalonia.Converters;
using LauncherKit.Desktop.Avalonia.Localization;
using LauncherKit.Desktop.Localization;
using LauncherKit.Core.FileStorage;
using LauncherKit.Core.UserSettings;

namespace LauncherKit.Desktop.Avalonia.Tests.Converters;

[TestFixture]
public sealed class EnumToStringConverterTests
{
	[TestCase(StoredFileStatus.Complete, DesktopStrings.StoredComplete)]
	[TestCase(StoredFileStatus.Partial, DesktopStrings.StoredPartial)]
	[TestCase(FileStoragePeriod.Day, DesktopStrings.StorageDay)]
	[TestCase(FileStoragePeriod.Unlimited, DesktopStrings.StorageUnlimited)]
	public void Convert_EnumValue_ReturnsLocalizedResource(object value, string key)
	{
		// Act
		var result = EnumToStringConverter.Instance.Convert(value, typeof(string), null, CultureInfo.InvariantCulture);

		// Assert
		Assert.That(result, Is.EqualTo(LocalizationMarkup.DesktopStrings[key].Value));
	}
}
