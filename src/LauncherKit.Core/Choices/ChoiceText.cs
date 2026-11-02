namespace LauncherKit.Core.Choices;

/// <summary>Content supplied by a feed or FOMOD, or a culture-neutral description of kit-owned choice text.</summary>
public union ChoiceText(
	ChoiceText.Content,
	ChoiceText.NoneOption,
	ChoiceText.YesOption,
	ChoiceText.GraphPackStepTitle,
	ChoiceText.ExtraDownloadSize,
	ChoiceText.SelectRequiredModTitle,
	ChoiceText.RequiredModsStepTitle,
	ChoiceText.SelectOneModSubtitle,
	ChoiceText.UnavailableGraphPackUpdating,
	ChoiceText.UnavailableFomodOption,
	ChoiceText.ModpackComponentsTitle,
	ChoiceText.ComponentOptionDescription)
{
	public sealed record Content(string Value);
	public sealed record NoneOption;
	public sealed record YesOption;
	public sealed record GraphPackStepTitle;
	public sealed record ExtraDownloadSize(long Bytes);
	public sealed record SelectRequiredModTitle(IReadOnlyList<string> Mods);
	public sealed record RequiredModsStepTitle;
	public sealed record SelectOneModSubtitle;
	public sealed record UnavailableGraphPackUpdating;
	public sealed record UnavailableFomodOption;
	public sealed record ModpackComponentsTitle;
	public sealed record ComponentOptionDescription(string Name, string Recommendation, long? DownloadBytes, bool IsMod);
}
