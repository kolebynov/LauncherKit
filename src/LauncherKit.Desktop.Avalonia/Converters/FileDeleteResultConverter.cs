using System.Globalization;
using LauncherKit.Desktop.Avalonia.Localization;
using LauncherKit.Desktop.Localization;
using LauncherKit.Desktop.ViewModels;
using Avalonia.Data.Converters;

namespace LauncherKit.Desktop.Avalonia.Converters;

public sealed class FileDeleteResultConverter : IValueConverter
{
	public static FileDeleteResultConverter Instance { get; } = new();

	public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
	{
		DownloadsViewModel.FileDeleteResult result => new FileDeleteResultTexts(LocalizationMarkup.DesktopStrings).Format(result.FileName, result.Result),
		null => null,
		_ => throw new ArgumentException("Expected a file deletion result", nameof(value)),
	};

	public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
