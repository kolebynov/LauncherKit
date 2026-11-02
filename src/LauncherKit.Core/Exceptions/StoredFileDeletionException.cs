namespace LauncherKit.Core.Exceptions;

// The path is mandatory UI-mapping data; message-only constructors would create an invalid exception.
#pragma warning disable CA1032
public sealed class StoredFileDeletionException : IOException
{
	public string Path { get; }

	public StoredFileDeletionException(string path, Exception innerException)
		: base($"Failed to delete file {path}", innerException)
	{
		Path = path;
	}
}
