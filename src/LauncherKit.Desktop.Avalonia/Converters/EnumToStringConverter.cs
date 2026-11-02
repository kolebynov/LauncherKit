using System.Globalization;
using LauncherKit.Desktop.Avalonia.Converters;
using LauncherKit.Desktop.Avalonia.Localization;
using LauncherKit.Desktop.Localization;
using Avalonia.Data.Converters;
using LauncherKit.Core.FileStorage;
using LauncherKit.Core.UserSettings;

namespace LauncherKit.Desktop.Avalonia.Converters;

public sealed class EnumToStringConverter : IValueConverter
{
	public static EnumToStringConverter Instance { get; } = new();

	public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
	{
		return value switch
		{
			FileStoragePeriod fileStoragePeriod => LocalizationMarkup.EnumTexts.Get(fileStoragePeriod).Value,
			StoredFileStatus storedFileStatus => LocalizationMarkup.EnumTexts.Get(storedFileStatus).Value,
#pragma warning disable CA1508
			_ => value?.ToString(),
#pragma warning restore CA1508
		};
	}

	public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
		throw new NotSupportedException();
}