﻿namespace LauncherKit.Core.Models;

#pragma warning disable CA1063
public abstract class BaseAsyncResult<TResult> : IDisposable
#pragma warning restore CA1063
{
	private readonly TaskCompletionSource<TResult> _resultTaskSource = new();
	private readonly IDisposable? _cancellationTokenRegistration;

	public Task<TResult> ResultTask => _resultTaskSource.Task;

	public void SetResult(TResult result)
	{
		_resultTaskSource.TrySetResult(result);
	}

#pragma warning disable CA1063
#pragma warning disable CA1816
	public void Dispose()
#pragma warning restore CA1816
#pragma warning restore CA1063
	{
		_cancellationTokenRegistration?.Dispose();
	}

	protected BaseAsyncResult(CancellationToken cancellationToken)
	{
		if (cancellationToken.CanBeCanceled)
		{
			_cancellationTokenRegistration = cancellationToken.Register(() => _resultTaskSource.TrySetCanceled());
		}
	}
}