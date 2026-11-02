namespace LauncherKit.Core.Exceptions;

// The path is mandatory UI-mapping data; message-only constructors would create an invalid exception.
#pragma warning disable CA1032
public sealed class StoredFileAccessDeniedException : UnauthorizedAccessException
{
	public string Path { get; }

	public StoredFileAccessDeniedException(string path)
		: base($"Cannot delete file {path}: access denied")
	{
		Path = path;
	}
}
