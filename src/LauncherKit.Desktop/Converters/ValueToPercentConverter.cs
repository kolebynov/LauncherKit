using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;

namespace LauncherKit.Desktop.Converters;

public sealed class ValueToPercentConverter : IValueConverter
{
	public static ValueToPercentConverter Instance { get; } = new();

	public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
	{
		double? val = value switch
		{
			long l => l,
			int i => i,
			double d => d,
			_ => null,
		};

		if (val is not { } doubleValue)
		{
			return new BindingNotification(new InvalidCastException("Value has invalid type"), BindingErrorType.Error);
		}

		if (double.IsNaN(doubleValue) || double.IsInfinity(doubleValue) || double.IsNegative(doubleValue))
		{
			doubleValue = 0;
		}

		return $"{doubleValue * 100:F0}%";
	}

	public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}