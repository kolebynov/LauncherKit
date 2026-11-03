using LauncherKit.Desktop.Localization;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.Localization;

namespace LauncherKit.Desktop.Avalonia.Localization;

/// <summary>
/// The application's single composition bridge for XAML text. <see cref="Configure"/> must run before any view or
/// converter reads text; there is no fallback localizer.
/// </summary>
public static class LocalizationMarkup
{
	private static Texts? _texts;

	private static Texts Current => _texts ?? throw new InvalidOperationException(
		$"{nameof(LocalizationMarkup)}.{nameof(Configure)} must be called before localized text is used.");

	public static IStringLocalizer<DesktopStrings> DesktopStrings => Current.Desktop;

	public static TaskTexts TaskTexts => Current.Tasks;

	public static ChoiceTexts ChoiceTexts => Current.Choices;

	public static ExceptionTexts ExceptionTexts => Current.Exceptions;

	public static EnumTexts EnumTexts => Current.Enums;

	public static SetupAccountFactTexts SetupAccountFactTexts => Current.SetupAccountFacts;

	public static void Configure(IStringLocalizer<DesktopStrings> desktop)
	{
		ArgumentNullException.ThrowIfNull(desktop);
		_texts = new Texts(desktop, new TaskTexts(desktop), new ChoiceTexts(desktop), new ExceptionTexts(desktop),
			new EnumTexts(desktop), new SetupAccountFactTexts(desktop));
	}

	internal static string Desktop(string key) => DesktopStrings[key].Value;

	private sealed record Texts(IStringLocalizer<DesktopStrings> Desktop, TaskTexts Tasks, ChoiceTexts Choices,
		ExceptionTexts Exceptions, EnumTexts Enums, SetupAccountFactTexts SetupAccountFacts);
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

