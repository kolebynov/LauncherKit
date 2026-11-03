using System.Globalization;

namespace LauncherKit.Desktop.Localization;

public static class LanguageCulture
{
	private static readonly CultureInfo OperatingSystemCulture = CultureInfo.CurrentUICulture;

	/// <summary>The languages the user can choose (<c>App:Language</c>): the kit's translations.</summary>
	public static IReadOnlyList<string> Languages { get; } = ["en", "ru"];

	/// <summary>Whether <paramref name="language"/> is a chosen language; anything else means none was chosen yet.</summary>
	public static bool IsSupported(string? language) => language is not null && Languages.Contains(language);

	/// <summary>Applies the language; until one is chosen the operating system's UI culture is used.</summary>
	public static void Apply(string? language)
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
