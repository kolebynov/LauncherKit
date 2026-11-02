using System.Runtime.CompilerServices;

namespace LauncherKit.TestSupport;

/// <summary>
/// Bounded awaits: a hung system under test fails with a clear message instead of hanging the run.
/// The timeout is a failure guard, not a timing assertion.
/// </summary>
public static class TaskAssert
{
	private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(5);

	public static async Task<T> WithinTimeout<T>(Task<T> task, [CallerMemberName] string? caller = null)
	{
		ArgumentNullException.ThrowIfNull(task);
		await WaitWithinTimeout(task, caller);
		return await task;
	}

	public static async Task WithinTimeout(Task task, [CallerMemberName] string? caller = null)
	{
		ArgumentNullException.ThrowIfNull(task);
		await WaitWithinTimeout(task, caller);
		await task;
	}

	private static async Task WaitWithinTimeout(Task task, string? caller)
	{
		try
		{
			await task.WaitAsync(DefaultTimeout);
		}
		catch (TimeoutException) when (!task.IsCompleted)
		{
			Assert.Fail($"{caller}: task did not complete within {DefaultTimeout.TotalSeconds}s");
		}
	}

	/// <summary>
	/// Polls <paramref name="condition"/> until it holds; used only for state a background worker sets
	/// without exposing a completion signal.
	/// </summary>
	public static async Task Eventually(Func<bool> condition, string message, [CallerMemberName] string? caller = null)
	{
		ArgumentNullException.ThrowIfNull(condition);
		var deadline = Environment.TickCount64 + (long)DefaultTimeout.TotalMilliseconds;
		while (!condition())
		{
			if (Environment.TickCount64 > deadline)
			{
				Assert.Fail($"{caller}: {message} did not happen within {DefaultTimeout.TotalSeconds}s");
			}

			await Task.Yield();
		}
	}
}
