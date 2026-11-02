using System.Globalization;
using LauncherKit.Desktop.Avalonia.Converters;
using LauncherKit.Desktop.Avalonia.Localization;
using LauncherKit.Desktop.Localization;
using Avalonia.Data.Converters;
using LauncherKit.Core.Choices;

namespace LauncherKit.Desktop.Avalonia.Converters;

public sealed class ChoiceTextConverter : IValueConverter
{
	public static ChoiceTextConverter Instance { get; } = new();

	public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
	{
		ChoiceText text => LocalizationMarkup.ChoiceTexts.Get(text).Value,
		null => null,
		_ => throw new ArgumentException("Expected a choice text", nameof(value)),
	};

	public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
