using System.Resources;
using LauncherKit.Desktop.Localization;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;

namespace LauncherKit.Desktop.Avalonia.Localization;

/// <summary>The application's single composition bridge for XAML text; configure before loading views.</summary>
public static class LocalizationMarkup
{
	private static IStringLocalizer<DesktopStrings> _desktop = new ExactResourceStringLocalizer<DesktopStrings>(
		new ResourceManager("LauncherKit.Desktop.Localization.DesktopStrings", typeof(DesktopStrings).Assembly), null, NullLogger.Instance);

	public static IStringLocalizer<DesktopStrings> DesktopStrings => _desktop;


	public static TaskTexts TaskTexts { get; private set; } = new(_desktop);

	public static ChoiceTexts ChoiceTexts { get; private set; } = new(_desktop);

	public static ExceptionTexts ExceptionTexts { get; private set; } = new(_desktop);

	public static EnumTexts EnumTexts { get; private set; } = new(_desktop);

	public static SetupAccountFactTexts SetupAccountFactTexts { get; private set; } = new(_desktop);

	public static void Configure(IStringLocalizer<DesktopStrings> desktop)
	{
		ArgumentNullException.ThrowIfNull(desktop);
		_desktop = desktop;
		TaskTexts = new TaskTexts(desktop);
		ChoiceTexts = new ChoiceTexts(desktop);
		ExceptionTexts = new ExceptionTexts(desktop);
		EnumTexts = new EnumTexts(desktop);
		SetupAccountFactTexts = new SetupAccountFactTexts(desktop);
	}

	internal static string Desktop(string key) => DesktopStrings[key].Value;

}

public sealed class DesktopStringExtension : MarkupExtension
{
	public DesktopStringExtension()
	{
	}

	public DesktopStringExtension(string key) => Key = key;

	public string Key { get; set; } = string.Empty;

	public override object ProvideValue(IServiceProvider serviceProvider) => LocalizationMarkup.Desktop(Key);
}

