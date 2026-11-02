﻿namespace LauncherKit.Core.Extensions;

public static class DelegateExtensions
{
	public static Action Debounce(this Action func, TimeSpan delay, TimeProvider timeProvider)
	{
		var debounced = Debounce<object?>(_ => func(), delay, timeProvider);
		return () => debounced(null);
	}

	public static Action<T> Debounce<T>(this Action<T> func, TimeSpan delay, TimeProvider timeProvider)
	{
		CancellationTokenSource? cancelTokenSource = null;

		return arg =>
		{
			cancelTokenSource?.Cancel();
			cancelTokenSource?.Dispose();
			cancelTokenSource = new CancellationTokenSource();

			Task.Delay(delay, timeProvider, cancelTokenSource.Token)
				.ContinueWith(t =>
				{
					if (t.IsCompletedSuccessfully)
					{
						func(arg);
					}
				}, TaskScheduler.Default);
		};
	}
}
