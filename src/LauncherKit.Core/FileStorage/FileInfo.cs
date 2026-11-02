using LauncherKit.Core.Models;

namespace LauncherKit.Core.FileStorage;

internal sealed class FileInfo
{
	public required string FileName { get; init; }

	public required string Path { get; init; }

	public required long Size { get; init; }

	public required DateTimeOffset CreatedAt { get; init; }

	public required Sha256Hash? Hash { get; init; }

	public DateTimeOffset? FinishedAt { get; init; }
}