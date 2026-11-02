﻿namespace LauncherKit.Core.FileStorage;

internal sealed class FilePart
{
	public required string FileName { get; init; }

	public required long Index { get; init; }

	public required long Size { get; init; }

	public required long WrittenBytes { get; init; }

	/// <summary>When the part was allocated or last had bytes written to it.</summary>
	public required DateTimeOffset LastWriteAt { get; init; }
}