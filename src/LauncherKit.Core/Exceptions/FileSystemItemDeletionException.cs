namespace LauncherKit.Core.Exceptions;

// The path is mandatory UI-mapping data; message-only constructors would create an invalid exception.
#pragma warning disable CA1032
public sealed class FileSystemItemDeletionException : InvalidOperationException
{
	public string Path { get; }

	public FileSystemItemDeletionException(string path, Exception innerException)
		: base($"Failed to delete file system item {path}", innerException)
	{
		Path = path;
	}
}
