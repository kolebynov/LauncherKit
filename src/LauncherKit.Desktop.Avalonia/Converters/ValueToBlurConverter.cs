using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace LauncherKit.Desktop.Avalonia.Converters;

public sealed class ValueToBlurConverter : IValueConverter
{
	public static ValueToBlurConverter Instance { get; } = new();

	public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
	{
		var needBlur = value switch
		{
			bool b => b,
			int i => i != 0,
			long l => l != 0,
			_ => false,
		};

		return needBlur ? new BlurEffect { Radius = double.TryParse(parameter?.ToString(), out var v) ? v : 5 } : null;
	}

	public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
		throw new NotSupportedException();
}