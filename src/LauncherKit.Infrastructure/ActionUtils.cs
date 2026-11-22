namespace LauncherKit.Infrastructure;

public static class ActionUtils
{
	public static void ExecuteAndSupressError(Action action)
	{
		try
		{
			action();
		}
		catch
		{
		}
	}
}