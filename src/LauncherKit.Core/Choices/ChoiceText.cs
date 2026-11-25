using NuGet.Versioning;
using LauncherKit.Core.ModDependencies;

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
	ChoiceText.ComponentOptionDescription,
	ChoiceText.SelectModDependencyTitle,
	ChoiceText.ModDependencyStepTitle,
	ChoiceText.ModDependencyOptionDescription,
	ChoiceText.ModDependencyKeptDescription,
	ChoiceText.UnavailableModDependency)
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
	/// <summary>Choosing which mod to install for a dependency of the mod <paramref name="ModName"/> (display name).</summary>
	public sealed record SelectModDependencyTitle(string ModName);
	/// <summary>
	/// The step of the dependency numbered <paramref name="Number"/> (from 1, in feed order) of the mod whose unique
	/// feed name is <paramref name="FeedName"/>; distinct for every dependency choice of an installation, even when mods
	/// share a display name, so automation selects steps by it.
	/// </summary>
	public sealed record ModDependencyStepTitle(string FeedName, int Number);
	/// <summary>
	/// Installing <paramref name="TargetVersion"/> of a dependency, updating <paramref name="InstalledVersion"/> when it
	/// is installed.
	/// </summary>
	public sealed record ModDependencyOptionDescription(NuGetVersion TargetVersion, NuGetVersion? InstalledVersion);
	/// <summary>A dependency met by keeping its installed <paramref name="Version"/>.</summary>
	public sealed record ModDependencyKeptDescription(NuGetVersion Version);
	public sealed record UnavailableModDependency(ModDependencyUnavailableReason Reason);
}
