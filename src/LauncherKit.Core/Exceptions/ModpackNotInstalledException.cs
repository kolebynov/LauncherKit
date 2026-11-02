namespace LauncherKit.Core.Exceptions;

#pragma warning disable CA1032
public sealed class ModpackNotInstalledException : Exception
{
	public override string Message => "STB is not installed";
}