using System.Text.Json.Serialization;
using NuGet.Versioning;

namespace LauncherKit.Core.Feed;

[JsonConverter(typeof(JsonStringEnumConverter<InstallComponentKind>))]
public enum InstallComponentKind
{
	GraphPack,
	Mod,
}

public sealed class InstallComponent
{
	public required string Id { get; init; }
	public required InstallComponentKind Kind { get; init; }
	public string? ModName { get; init; }
	public required LocalizedText Title { get; init; }
	public required LocalizedText Description { get; init; }
	public required VersionRange SupportedModpackVersion { get; init; }
	public int Order { get; init; }
}

public sealed class Prerequisite
{
	public required string Id { get; init; }
	public required DownloadInfo DownloadInfo { get; init; }
	public required string Arguments { get; init; }
}
