using System.Globalization;
using LauncherKit.Desktop.Avalonia.Converters;
using Avalonia.Data;
using Avalonia.Data.Converters;
using LauncherKit.Core.Utils;
using LauncherKit.Infrastructure;

namespace LauncherKit.Desktop.Avalonia.Converters;

public sealed class BytesToStringConverter : IValueConverter
{
	public static BytesToStringConverter Instance { get; } = new();

	object? IValueConverter.Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
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

		if (doubleValue < 0)
		{
			return null;
		}

		BytesFormatter.Measurement? m = parameter?.ToString() switch
		{
			"size" or null => BytesFormatter.Measurement.Size.Instance,
			"speed" => BytesFormatter.Measurement.Speed.Instance,
			_ => null,
		};

		if (m is not { } measurement)
		{
			return new BindingNotification(new ArgumentOutOfRangeException("Invalid converter parameter", parameter, nameof(parameter)), BindingErrorType.Error);
		}

		return BytesFormatter.Format(doubleValue, measurement);
	}

	object? IValueConverter.ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
		throw new NotSupportedException();
}