namespace LauncherKit.Core.FileStorage;

/// <summary>A file kept in the downloads directory, finished or still being downloaded.</summary>
public sealed class StoredFileInfo
{
	public required string FileName { get; init; }

	public required long Size { get; init; }

	public required StoredFileStatus Status { get; init; }

	/// <summary>
	/// When the file was finished; for a partial file, the latest write to any of its parts (its creation
	/// time when no part has been allocated yet).
	/// </summary>
	public required DateTimeOffset LastWriteAt { get; init; }
}
