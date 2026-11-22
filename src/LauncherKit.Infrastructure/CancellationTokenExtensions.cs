namespace LauncherKit.Infrastructure;

public static class CancellationTokenExtensions
{
	extension(CancellationToken cancellationToken)
	{
		public async Task WaitForCancel(CancellationToken cancelToken)
		{
			if (cancellationToken.IsCancellationRequested)
			{
				return;
			}

			var tcs = new TaskCompletionSource();
			using var sourceSub = cancellationToken.Register(() => tcs.TrySetResult());
			using var cancelSub = cancelToken.Register(() => tcs.TrySetCanceled());

			await tcs.Task;
		}
	}
}