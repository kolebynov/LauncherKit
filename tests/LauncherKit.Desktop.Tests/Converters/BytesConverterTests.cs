using System.Globalization;
using LauncherKit.Desktop.Converters;
using Avalonia.Data;

namespace LauncherKit.Desktop.Tests.Converters;

[TestFixture]
public sealed class BytesConverterTests
{
	private const long GigaByte = 1024L * 1024 * 1024;

	[Test]
	public void Convert_WithoutUnitParameter_ReturnsValueUnchanged()
	{
		// Arrange
		var sut = new BytesConverter();

		// Act
		var result = sut.Convert(1536L, typeof(long), null, CultureInfo.InvariantCulture);

		// Assert
		Assert.That(result, Is.EqualTo(1536L));
	}

	[TestCase("kb", 1024L * 1024)]
	[TestCase("mb", 1024L)]
	[TestCase("gb", 1L)]
	[TestCase("unsupported", GigaByte)]
	public void Convert_UnitParameter_DividesByRequestedUnit(string parameter, long expected)
	{
		// Arrange
		var sut = new BytesConverter();

		// Act
		var result = sut.Convert(GigaByte, typeof(long), parameter, CultureInfo.InvariantCulture);

		// Assert
		Assert.That(result, Is.EqualTo(expected));
	}

	[TestCase(1023L, 0L)]
	[TestCase(1024L, 1L)]
	[TestCase(2047L, 1L)]
	[TestCase(2048L, 2L)]
	public void Convert_IntegralValueInKilobytes_TruncatesTowardsZero(long value, long expected)
	{
		// Arrange
		var sut = new BytesConverter();

		// Act
		var result = sut.Convert(value, typeof(long), "kb", CultureInfo.InvariantCulture);

		// Assert
		Assert.That(result, Is.EqualTo(expected));
	}

	[Test]
	public void Convert_DoubleValueInMegabytes_KeepsFraction()
	{
		// Arrange
		var sut = new BytesConverter();

		// Act
		var result = sut.Convert(1024.0 * 1024 * 1.5, typeof(double), "mb", CultureInfo.InvariantCulture);

		// Assert
		Assert.That(result, Is.EqualTo(1.5));
	}

	[Test]
	public void Convert_IntValue_ConvertsResultToTargetType()
	{
		// Arrange
		var sut = new BytesConverter();

		// Act
		var result = sut.Convert(2048, typeof(string), "kb", CultureInfo.InvariantCulture);

		// Assert
		Assert.That(result, Is.EqualTo("2"));
	}

	[Test]
	public void Convert_UnsupportedValueType_ReturnsErrorNotification()
	{
		// Arrange
		var sut = new BytesConverter();

		// Act
		var result = sut.Convert("1024", typeof(long), "kb", CultureInfo.InvariantCulture);

		// Assert
		Assert.That(result, Is.InstanceOf<BindingNotification>());
		var notification = (BindingNotification)result!;
		Assert.Multiple(() =>
		{
			Assert.That(notification.ErrorType, Is.EqualTo(BindingErrorType.Error));
			Assert.That(notification.Error, Is.InstanceOf<InvalidCastException>());
		});
	}

	[TestCase("kb", 2048L)]
	[TestCase("mb", 2L * 1024 * 1024)]
	[TestCase("gb", 2L * 1024 * 1024 * 1024)]
	[TestCase(null, 2L)]
	public void ConvertBack_UnitParameter_MultipliesByRequestedUnit(string? parameter, long expected)
	{
		// Arrange
		var sut = new BytesConverter();

		// Act
		var result = sut.ConvertBack(2L, typeof(long), parameter, CultureInfo.InvariantCulture);

		// Assert
		Assert.That(result, Is.EqualTo(expected));
	}

	[Test]
	public void ConvertBack_UnsupportedValueType_ReturnsErrorNotification()
	{
		// Arrange
		var sut = new BytesConverter();

		// Act
		var result = sut.ConvertBack(new object(), typeof(long), "kb", CultureInfo.InvariantCulture);

		// Assert
		Assert.That(result, Is.InstanceOf<BindingNotification>());
		var notification = (BindingNotification)result!;
		Assert.Multiple(() =>
		{
			Assert.That(notification.ErrorType, Is.EqualTo(BindingErrorType.Error));
			Assert.That(notification.Error, Is.InstanceOf<InvalidCastException>());
		});
	}
}
