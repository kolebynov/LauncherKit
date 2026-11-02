﻿namespace LauncherKit.Core.Utils;

public sealed class LockedResource<T> : IDisposable
{
	private readonly T _resource;
	private readonly SemaphoreSlim _lock = new(1, 1);

	public LockedResource(T resource)
	{
		_resource = resource;
	}

	public AcquiredLock Acquire()
	{
		_lock.Wait();
		return new AcquiredLock(this);
	}

	public async Task<AcquiredLock> AcquireAsync(CancellationToken cancellationToken)
	{
		await _lock.WaitAsync(cancellationToken);
		return new AcquiredLock(this);
	}

	public void Dispose()
	{
		_lock.Dispose();
	}

	public readonly struct AcquiredLock : IDisposable
	{
		private readonly LockedResource<T> _lockedResource;

		public T Resource => _lockedResource._resource;

		public AcquiredLock(LockedResource<T> lockedResource)
		{
			_lockedResource = lockedResource;
		}

		public void Dispose()
		{
			_lockedResource._lock.Release();
		}
	}
}