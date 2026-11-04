using System.Globalization;
using LauncherKit.Desktop.Localization;
using Avalonia.Data;
using Avalonia.Data.Converters;

namespace LauncherKit.Desktop.Converters;

public sealed class ExceptionDisplayConverter : IValueConverter
{
	public static ExceptionDisplayConverter Instance { get; } = new();

	public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
		value is Exception exception
			? LocalizationMarkup.ExceptionTexts.Get(exception).Value
			: new BindingNotification(new ArgumentOutOfRangeException(nameof(value), value, "Expected an exception"), BindingErrorType.Error);

	public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
		throw new NotSupportedException();
}