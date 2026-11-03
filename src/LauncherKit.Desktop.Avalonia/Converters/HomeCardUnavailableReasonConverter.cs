using System.Globalization;
using LauncherKit.Desktop.Avalonia.Localization;
using LauncherKit.Desktop.Localization;
using LauncherKit.Desktop.ViewModels;
using Avalonia.Data.Converters;

namespace LauncherKit.Desktop.Avalonia.Converters;

public sealed class HomeCardUnavailableReasonConverter : IValueConverter
{
	public static HomeCardUnavailableReasonConverter Instance { get; } = new();

	public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
	{
		var texts = new HomeCardFactTexts(LocalizationMarkup.DesktopStrings);
		return value switch
		{
			ModpackInstallUnavailableReason reason => texts.Get(reason).Value,
			ModCardViewModel.InstallUnavailableReason reason => texts.Get(reason).Value,
			null => null,
			_ => throw new ArgumentException("Expected an installation unavailable reason", nameof(value)),
		};
	}

	public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
