namespace LauncherKit.Core.Exceptions;

// The profile is mandatory UI-mapping data; message-only constructors would create an invalid exception.
#pragma warning disable CA1032
public sealed class LocalSavesDisabledException : InvalidOperationException
{
	public string Profile { get; }

	public LocalSavesDisabledException(string profile)
		: base($"Local saves are disabled in profile {profile}")
	{
		Profile = profile;
	}
}
