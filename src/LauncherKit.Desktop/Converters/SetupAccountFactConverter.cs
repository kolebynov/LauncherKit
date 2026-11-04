using System.Collections;
using LauncherKit.Desktop.Localization;
using LauncherKit.Desktop.ViewModels;
using LauncherKit.Desktop.ViewModels.Setup;
using System.Globalization;
using Avalonia.Data.Converters;
using LauncherKit.Core.Feed;

namespace LauncherKit.Desktop.Converters;

public sealed class SetupAccountFactConverter : IValueConverter
{
	public static SetupAccountFactConverter Instance { get; } = new();

	public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
	{
		var texts = LocalizationMarkup.SetupAccountFactTexts;
		return value switch
		{
			DirectoryValidationError error => texts.Get(error).Value,
			DiscordMembership membership => texts.Get(membership).Value,
			ActivateBetaCodeResult result => texts.Get(result).Value,
			SetupError error => texts.Get(error).Value,
			string text => text,
			IEnumerable errors => errors.Cast<object?>().Select(error => Convert(error, typeof(string), null, culture)).ToArray(),
			_ => value,
		};
	}

	public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
		throw new NotSupportedException();
}
