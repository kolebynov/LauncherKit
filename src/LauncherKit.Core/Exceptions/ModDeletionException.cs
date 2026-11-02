namespace LauncherKit.Core.Exceptions;

// The mod name is mandatory UI-mapping data; message-only constructors would create an invalid exception.
#pragma warning disable CA1032
public sealed class ModDeletionException : InvalidOperationException
{
	public string ModName { get; }

	public ModDeletionException(string modName, Exception innerException)
		: base($"Failed to delete mod {modName}", innerException)
	{
		ModName = modName;
	}
}
