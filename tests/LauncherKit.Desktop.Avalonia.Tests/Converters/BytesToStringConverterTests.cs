using System.Globalization;
using LauncherKit.Desktop.Avalonia.Converters;
using Avalonia.Data;
using Avalonia.Data.Converters;

namespace LauncherKit.Desktop.Avalonia.Tests.Converters;

// The converter formats with interpolation, i.e. with the ambient culture rather than the culture
// argument, so the fixture pins the culture to keep the expected decimal separator stable.
[TestFixture]
[SetCulture("en-US")]
public sealed class BytesToStringConverterTests
{
	[TestCase(0L, "0.00 B")]
	[TestCase(1023L, "1023.00 B")]
	[TestCase(1024L, "1.00 KB")]
	[TestCase(1023L * 1024, "1023.00 KB")]
	[TestCase(1024L * 1024, "1.00 MB")]
	[TestCase(1024L * 1024 * 1024, "1.00 GB")]
	[TestCase(2048L * 1024 * 1024 * 1024, "2048.00 GB")]
	public void Convert_UnitBoundaries_FormatsInLargestFittingUnit(long value, string expected)
	{
		// Arrange
		IValueConverter sut = new BytesToStringConverter();

		// Act
		var result = sut.Convert(value, typeof(string), null, CultureInfo.InvariantCulture);

		// Assert
		Assert.That(result, Is.EqualTo(expected));
	}

	[TestCase(null, "1.00 KB")]
	[TestCase("size", "1.00 KB")]
	[TestCase("speed", "1.00 KB/s")]
	public void Convert_MeasurementParameter_SelectsUnitSuffix(string? parameter, string expected)
	{
		// Arrange
		IValueConverter sut = new BytesToStringConverter();

		// Act
		var result = sut.Convert(1024L, typeof(string), parameter, CultureInfo.InvariantCulture);

		// Assert
		Assert.That(result, Is.EqualTo(expected));
	}

	[TestCase(1024)]
	[TestCase(1024L)]
	[TestCase(1024.0)]
	public void Convert_NumericValueTypes_FormatIdentically(object value)
	{
		// Arrange
		IValueConverter sut = new BytesToStringConverter();

		// Act
		var result = sut.Convert(value, typeof(string), null, CultureInfo.InvariantCulture);

		// Assert
		Assert.That(result, Is.EqualTo("1.00 KB"));
	}

	[Test]
	public void Convert_NegativeValue_ReturnsNull()
	{
		// Arrange
		IValueConverter sut = new BytesToStringConverter();

		// Act
		var result = sut.Convert(-1L, typeof(string), null, CultureInfo.InvariantCulture);

		// Assert
		Assert.That(result, Is.Null);
	}

	[Test]
	public void Convert_UnknownMeasurementParameter_ReturnsErrorNotification()
	{
		// Arrange
		IValueConverter sut = new BytesToStringConverter();

		// Act
		var result = sut.Convert(1024L, typeof(string), "weight", CultureInfo.InvariantCulture);

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
	public void Convert_UnsupportedValueType_ReturnsErrorNotification()
	{
		// Arrange
		IValueConverter sut = new BytesToStringConverter();

		// Act
		var result = sut.Convert("1024", typeof(string), null, CultureInfo.InvariantCulture);

		// Assert
		Assert.That(result, Is.InstanceOf<BindingNotification>());
		var notification = (BindingNotification)result!;
		Assert.Multiple(() =>
		{
			Assert.That(notification.ErrorType, Is.EqualTo(BindingErrorType.Error));
			Assert.That(notification.Error, Is.InstanceOf<InvalidCastException>());
		});
	}

	[Test]
	public void ConvertBack_AnyValue_ThrowsNotSupported()
	{
		// Arrange
		IValueConverter sut = new BytesToStringConverter();

		// Act & Assert
		Assert.That(
			() => sut.ConvertBack("1.00 KB", typeof(long), null, CultureInfo.InvariantCulture),
			Throws.TypeOf<NotSupportedException>());
	}
}
