using Microsoft.Extensions.Localization;
using LauncherKit.Core.Choices;
using LauncherKit.Core.Utils;
using LauncherKit.Infrastructure;

namespace LauncherKit.Desktop.Localization;

public sealed class ChoiceTexts(IStringLocalizer<DesktopStrings> strings)
{
	public LocalizedString Get(ChoiceText text) => text switch
	{
		ChoiceText.Content(var value) => new LocalizedString(nameof(ChoiceText.Content), value),
		ChoiceText.NoneOption => strings[DesktopStrings.NoneOption],
		ChoiceText.YesOption => strings[DesktopStrings.YesOption],
		ChoiceText.ExtraDownloadSize(var bytes) => strings[DesktopStrings.ExtraDownloadSize, BytesFormatter.Format(bytes, BytesFormatter.Measurement.Size.Instance)],
		ChoiceText.SelectRequiredModTitle(var mods) => strings[DesktopStrings.SelectRequiredModTitle, string.Join(", ", mods)],
		ChoiceText.RequiredModsStepTitle => strings[DesktopStrings.RequiredModsStepTitle],
		ChoiceText.SelectOneModSubtitle => strings[DesktopStrings.SelectOneModSubtitle],
		ChoiceText.UnavailableModNotUpdated => strings[DesktopStrings.ModNotUpdated],
		ChoiceText.UnavailableFomodOption => strings[DesktopStrings.UnavailableFomodOption],
		ChoiceText.ModpackComponentsTitle => strings[DesktopStrings.ModpackComponentsTitle],
		ChoiceText.ComponentOptionDescription(var name, var optionDescription, var downloadBytes) =>
			ComponentDescription(name, optionDescription, downloadBytes),
	};

	private LocalizedString ComponentDescription(string name, string? optionDescription, long? downloadBytes)
	{
		var description = optionDescription == null
			? strings[DesktopStrings.ComponentModOptionDescription, name]
			: strings[DesktopStrings.ComponentOptionDescription, name, optionDescription];
		if (downloadBytes is not { } bytes)
		{
			return description;
		}

		var size = Get(new ChoiceText.ExtraDownloadSize(bytes));
		return new LocalizedString(description.Name, $"{description.Value}. {size.Value}", description.ResourceNotFound || size.ResourceNotFound);
	}
}
