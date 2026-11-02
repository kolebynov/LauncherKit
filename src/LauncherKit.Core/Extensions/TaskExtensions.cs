using Microsoft.Extensions.Logging;

namespace LauncherKit.Core.Extensions;

public static class TaskExtensions
{
	public static ValueTask<T> AsValueTask<T>(this Task<T> task) => new(task);

	extension(Task task)
	{
		public ValueTask AsValueTask() => new(task);

		public void Forget(ILogger? logger = null)
		{
			if (task.IsCompleted || task.IsCanceled || task.IsFaulted)
			{
				return;
			}

			_ = ForgetAwaited();
			return;

			async Task ForgetAwaited()
			{
				try
				{
					await task.ConfigureAwait(false);
				}
				catch (Exception e)
				{
					logger?.LogWarning(e, "Forgot task failed");
				}
			}
		}
	}
}