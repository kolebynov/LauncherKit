using System.Globalization;
using LauncherKit.Desktop.Avalonia.Converters;
using System.Numerics;
using Avalonia.Data;
using Avalonia.Data.Converters;
using LauncherKit.Core.Utils;

namespace LauncherKit.Desktop.Avalonia.Converters;

public sealed class BytesConverter : IValueConverter
{
	public static BytesConverter Instance { get; } = new();

	public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
	{
		var unitName = parameter?.ToString();
		return value switch
		{
			long l => ConvertImpl(l, unitName, true, targetType, culture),
			int i => ConvertImpl(i, unitName, true, targetType, culture),
			double d => ConvertImpl(d, unitName, true, targetType, culture),
			_ => new BindingNotification(new InvalidCastException("Value has invalid type"), BindingErrorType.Error),
		};
	}

	public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
	{
		var unitName = parameter?.ToString();
		return value switch
		{
			long l => ConvertImpl(l, unitName, false, targetType, culture),
			int i => ConvertImpl(i, unitName, false, targetType, culture),
			double d => ConvertImpl(d, unitName, false, targetType, culture),
			_ => new BindingNotification(new InvalidCastException("Value has invalid type"), BindingErrorType.Error),
		};
	}

	private static object ConvertImpl<T>(T value, string? unitName, bool divide, Type targetType, CultureInfo culture)
		where T : struct, INumber<T>
	{
		var unitSize = unitName switch
		{
			"kb" => BytesFormatter.KiloByte,
			"mb" => BytesFormatter.MegaByte,
			"gb" => BytesFormatter.GigaByte,
			_ => 1,
		};

		var result = divide ? BytesFormatter.ToUnit(value, unitSize) : BytesFormatter.FromUnit(value, unitSize);
		return System.Convert.ChangeType(result, targetType, culture);
	}
}