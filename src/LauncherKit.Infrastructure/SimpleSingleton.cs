namespace LauncherKit.Infrastructure;

public static class SimpleSingleton<T>
	where T : class, new()
{
	public static readonly T Value = new();
}
