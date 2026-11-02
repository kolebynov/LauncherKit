namespace LauncherKit.Core.Exceptions;

// The path is mandatory UI-mapping data; message-only constructors would create an invalid exception.
#pragma warning disable CA1032
public sealed class StoredFileInUseException : IOException
{
	public string Path { get; }

	public StoredFileInUseException(string path)
		: base($"Cannot delete file {path}: it is in use by another program")
	{
		Path = path;
	}
}
