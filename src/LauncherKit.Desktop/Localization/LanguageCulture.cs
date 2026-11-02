using System.Globalization;
using LauncherKit.Desktop.Localization;

namespace LauncherKit.Desktop.Localization;

public static class LanguageCulture
{
	private static readonly CultureInfo OperatingSystemCulture = CultureInfo.CurrentUICulture;

	public static void Apply(string language)
	{
		var culture = string.IsNullOrWhiteSpace(language) ? OperatingSystemCulture : CultureInfo.GetCultureInfo(language);
		CultureInfo.DefaultThreadCurrentUICulture = culture;
	}

	public static void ApplyToCurrentThread()
	{
		if (CultureInfo.DefaultThreadCurrentUICulture is { } culture)
			CultureInfo.CurrentUICulture = culture;
	}
}
