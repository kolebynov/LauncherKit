namespace LauncherKit.Core.Choices;

/// <summary>Content supplied by a feed or FOMOD, or a culture-neutral description of kit-owned choice text.</summary>
public union ChoiceText(
	ChoiceText.Content,
	ChoiceText.NoneOption,
	ChoiceText.YesOption,
	ChoiceText.ExtraDownloadSize,
	ChoiceText.SelectRequiredModTitle,
	ChoiceText.RequiredModsStepTitle,
	ChoiceText.SelectOneModSubtitle,
	ChoiceText.UnavailableModNotUpdated,
	ChoiceText.UnavailableFomodOption,
	ChoiceText.ModpackComponentsTitle,
	ChoiceText.ComponentOptionDescription)
{
	public sealed record Content(string Value);
	public sealed record NoneOption;
	public sealed record YesOption;
	public sealed record ExtraDownloadSize(long Bytes);
	public sealed record SelectRequiredModTitle(IReadOnlyList<string> Mods);
	public sealed record RequiredModsStepTitle;
	public sealed record SelectOneModSubtitle;
	/// <summary>The mod has no version supporting the modpack version yet.</summary>
	public sealed record UnavailableModNotUpdated;
	public sealed record UnavailableFomodOption;
	public sealed record ModpackComponentsTitle;
	/// <summary>Installing <paramref name="Name"/>, with its feed <paramref name="OptionDescription"/> and download size.</summary>
	public sealed record ComponentOptionDescription(string Name, string? OptionDescription, long? DownloadBytes);
}
