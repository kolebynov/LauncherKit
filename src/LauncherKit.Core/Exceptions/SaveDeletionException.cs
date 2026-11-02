namespace LauncherKit.Core.Exceptions;

// The file name is mandatory UI-mapping data; message-only constructors would create an invalid exception.
#pragma warning disable CA1032
public sealed class SaveDeletionException : InvalidOperationException
{
	public string FileName { get; }

	public SaveDeletionException(string fileName, Exception innerException)
		: base($"Failed to delete save {fileName}", innerException)
	{
		FileName = fileName;
	}
}
