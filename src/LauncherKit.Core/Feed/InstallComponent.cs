using System.Text.Json.Serialization;
using NuGet.Versioning;

namespace LauncherKit.Core.Feed;

[JsonConverter(typeof(JsonStringEnumConverter<InstallComponentKind>))]
public enum InstallComponentKind
{
	/// <summary>One of <see cref="InstallComponent.Options"/>, or none.</summary>
	ModChoice,

	/// <summary>Whether to install the mod <see cref="InstallComponent.ModName"/>.</summary>
	Mod,
}

public sealed class InstallComponent
{
	public required string Id { get; init; }
	public required InstallComponentKind Kind { get; init; }

	/// <summary>The mod of a <see cref="InstallComponentKind.Mod"/> component.</summary>
	public string? ModName { get; init; }

	/// <summary>The mods offered by a <see cref="InstallComponentKind.ModChoice"/> component, in display order.</summary>
	public IReadOnlyList<ModChoiceOption>? Options { get; init; }

	public required LocalizedText Title { get; init; }
	public required LocalizedText Description { get; init; }
	public required VersionRange SupportedModpackVersion { get; init; }
	public int Order { get; init; }
}

/// <summary>A mod offered by a <see cref="InstallComponentKind.ModChoice"/> component.</summary>
public sealed class ModChoiceOption
{
	public required string ModName { get; init; }

	/// <summary>
	/// Whether the option is selected by default; at most one option of a component is. An unavailable default leaves
	/// none selected.
	/// </summary>
	public bool IsDefault { get; init; }
}

public sealed class Prerequisite
{
	public required string Id { get; init; }
	public required DownloadInfo DownloadInfo { get; init; }
	public required string Arguments { get; init; }
}
