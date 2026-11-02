﻿namespace LauncherKit.Core.Extensions;

public static class SemaphoreSlimExtensions
{
	public static async Task<Lock> AcquireAsync(this SemaphoreSlim semaphoreSlim, CancellationToken cancellationToken)
	{
		await semaphoreSlim.WaitAsync(cancellationToken);
		return new Lock(semaphoreSlim);
	}

	public readonly struct Lock : IDisposable
	{
		private readonly SemaphoreSlim _semaphoreSlim;

		public Lock(SemaphoreSlim semaphoreSlim) => _semaphoreSlim = semaphoreSlim;

		public void Dispose() => _semaphoreSlim.Release();
	}
}