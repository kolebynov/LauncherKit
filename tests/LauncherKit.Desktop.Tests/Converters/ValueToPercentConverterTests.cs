using System.Globalization;
using LauncherKit.Desktop.Converters;
using Avalonia.Data;
using Avalonia.Data.Converters;

namespace LauncherKit.Desktop.Tests.Converters;

[TestFixture]
public sealed class ValueToPercentConverterTests
{
	[TestCase(0.0, "0%")]
	[TestCase(0.5, "50%")]
	[TestCase(1.0, "100%")]
	[TestCase(0.126, "13%")]
	public void Convert_FiniteNonNegativeValue_ReturnsRoundedPercentText(double value, string expected)
	{
		// Arrange
		var sut = new ValueToPercentConverter();

		// Act
		var result = sut.Convert(value, typeof(string), null, CultureInfo.InvariantCulture);

		// Assert
		Assert.That(result, Is.EqualTo(expected));
	}

	[TestCase(-0.5)]
	[TestCase(double.NaN)]
	[TestCase(double.PositiveInfinity)]
	public void Convert_NegativeOrNonFiniteValue_ReturnsZeroPercent(double value)
	{
		// Arrange
		var sut = new ValueToPercentConverter();

		// Act
		var result = sut.Convert(value, typeof(string), null, CultureInfo.InvariantCulture);

		// Assert
		Assert.That(result, Is.EqualTo("0%"));
	}

	[TestCase(2)]
	[TestCase(2L)]
	public void Convert_IntegralValue_ReturnsPercentText(object value)
	{
		// Arrange
		var sut = new ValueToPercentConverter();

		// Act
		var result = sut.Convert(value, typeof(string), null, CultureInfo.InvariantCulture);

		// Assert
		Assert.That(result, Is.EqualTo("200%"));
	}

	[Test]
	public void Convert_UnsupportedValueType_ReturnsErrorNotification()
	{
		// Arrange
		var sut = new ValueToPercentConverter();

		// Act
		var result = sut.Convert("0.5", typeof(string), null, CultureInfo.InvariantCulture);

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
		IValueConverter sut = new ValueToPercentConverter();

		// Act & Assert
		Assert.That(
			() => sut.ConvertBack("50%", typeof(double), null, CultureInfo.InvariantCulture),
			Throws.TypeOf<NotSupportedException>());
	}
}
