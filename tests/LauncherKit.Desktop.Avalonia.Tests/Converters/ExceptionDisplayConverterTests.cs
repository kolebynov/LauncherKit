using System.Globalization;
using LauncherKit.Desktop.Avalonia.Converters;
using LauncherKit.Desktop.Avalonia.Localization;
using LauncherKit.Desktop.Localization;
using Avalonia.Data;
using Avalonia.Data.Converters;
using NuGet.Versioning;
using LauncherKit.Core.Exceptions;

namespace LauncherKit.Desktop.Avalonia.Tests.Converters;

// Sizes are embedded through BytesToStringConverter, which formats with the ambient culture, so the
// fixture pins the culture to keep the expected decimal separator stable.
[TestFixture]
[SetCulture("en-US")]
public sealed class ExceptionDisplayConverterTests
{
	[Test]
	public void Convert_NotEnoughSpaceException_DescribesDiskAndRequiredAndAvailableSpace()
	{
		// Arrange
		var sut = new ExceptionDisplayConverter();
		var exception = new NotEnoughSpaceException("D:", 2L * 1024 * 1024 * 1024, 512L * 1024 * 1024);

		// Act
		var result = sut.Convert(exception, typeof(string), null, CultureInfo.InvariantCulture);

		// Assert
		Assert.That(result, Is.InstanceOf<string>());
		var text = (string)result!;
		Assert.Multiple(() =>
		{
			Assert.That(text, Does.Contain("D:"));
			Assert.That(text, Does.Contain("2.00 GB"));
			Assert.That(text, Does.Contain("512.00 MB"));
		});
	}

	[Test]
	public void Convert_ModpackNotInstalledException_ReturnsMappedTextInsteadOfExceptionMessage()
	{
		// Arrange
		var sut = new ExceptionDisplayConverter();
		var exception = new ModpackNotInstalledException();

		// Act
		var result = sut.Convert(exception, typeof(string), null, CultureInfo.InvariantCulture);

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(result, Is.InstanceOf<string>().And.Not.Empty);
			Assert.That(result, Is.Not.EqualTo(exception.Message));
		});
	}

	[Test]
	public void Convert_ModNotSupportedModpackVersionException_MentionsModAndModpackVersion()
	{
		// Arrange
		var sut = new ExceptionDisplayConverter();
		var exception = new ModNotSupportedModpackVersionException("SuperMod", new NuGetVersion("1.2.3"));

		// Act
		var result = sut.Convert(exception, typeof(string), null, CultureInfo.InvariantCulture);

		// Assert
		Assert.That(result, Is.InstanceOf<string>());
		var text = (string)result!;
		Assert.Multiple(() =>
		{
			Assert.That(text, Does.Contain("SuperMod"));
			Assert.That(text, Does.Contain("1.2.3"));
		});
	}

	[Test]
	public void Convert_ModOrganizerAlreadyRunningException_ReturnsMappedTextInsteadOfExceptionMessage()
	{
		// Arrange
		var sut = new ExceptionDisplayConverter();
		var exception = new ModOrganizerAlreadyRunningException();

		// Act
		var result = sut.Convert(exception, typeof(string), null, CultureInfo.InvariantCulture);

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(result, Is.InstanceOf<string>().And.Not.Empty);
			Assert.That(result, Is.Not.EqualTo(exception.Message));
		});
	}

	[Test]
	public void Convert_MappedExceptions_ProduceDistinctTexts()
	{
		// Arrange
		var sut = new ExceptionDisplayConverter();
		var culture = CultureInfo.InvariantCulture;

		// Act
		var texts = new[]
		{
			sut.Convert(new NotEnoughSpaceException("C:", 1024, 512), typeof(string), null, culture),
			sut.Convert(new ModpackNotInstalledException(), typeof(string), null, culture),
			sut.Convert(new ModNotSupportedModpackVersionException("Mod", new NuGetVersion("1.0.0")), typeof(string), null, culture),
			sut.Convert(new ModOrganizerAlreadyRunningException(), typeof(string), null, culture),
		};

		// Assert
		Assert.That(texts, Is.Unique);
	}

	[Test]
	public void Convert_UnmappedException_FormatsDiagnosticInLocalizedFallback()
	{
		// Arrange
		var sut = new ExceptionDisplayConverter();

		// Act
		var result = sut.Convert(new InvalidOperationException("something broke"), typeof(string), null, CultureInfo.InvariantCulture);

		// Assert
		Assert.That(result, Is.EqualTo(new ExceptionTexts(LocalizationMarkup.DesktopStrings).Get(new InvalidOperationException("something broke")).Value));
	}

	[Test]
	public void Convert_NonExceptionValue_ReturnsErrorNotification()
	{
		// Arrange
		var sut = new ExceptionDisplayConverter();

		// Act
		var result = sut.Convert(null, typeof(string), null, CultureInfo.InvariantCulture);

		// Assert
		Assert.That(result, Is.InstanceOf<BindingNotification>());
		var notification = (BindingNotification)result!;
		Assert.Multiple(() =>
		{
			Assert.That(notification.ErrorType, Is.EqualTo(BindingErrorType.Error));
			Assert.That(notification.Error, Is.InstanceOf<ArgumentOutOfRangeException>());
		});
	}

	[Test]
	public void ConvertBack_AnyValue_ThrowsNotSupported()
	{
		// Arrange
		IValueConverter sut = new ExceptionDisplayConverter();

		// Act & Assert
		Assert.That(
			() => sut.ConvertBack("text", typeof(Exception), null, CultureInfo.InvariantCulture),
			Throws.TypeOf<NotSupportedException>());
	}
}
