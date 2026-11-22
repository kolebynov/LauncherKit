using System.Diagnostics.CodeAnalysis;
using R3;

namespace LauncherKit.Desktop.SingleInstance;

/// <summary>
/// The owner's bounded FIFO of admitted launch requests. It exists before the services and is registered as their
/// singleton, so requests admitted during startup or a scope reload wait until the pump drains them.
/// </summary>
internal sealed class LaunchRequestQueue : ILaunchRequestQueue, IDisposable
{
	public const int Capacity = 32;

	private readonly Lock _lock = new();
	private readonly Queue<LaunchContext> _requests = new();
	private readonly Subject<Unit> _enqueued = new();
	private bool _completed;

	/// <summary>Raised after each admitted request, on the admitting thread.</summary>
	public Observable<Unit> Enqueued => _enqueued;

	/// <summary>Admits <paramref name="request"/> unless the queue is full or completed.</summary>
	public bool TryEnqueue(LaunchContext request)
	{
		lock (_lock)
		{
			if (_completed || _requests.Count >= Capacity) return false;
			_requests.Enqueue(request);
		}

		_enqueued.OnNext(Unit.Default);
		return true;
	}

	public bool TryDequeue([MaybeNullWhen(false)] out LaunchContext request)
	{
		lock (_lock) return _requests.TryDequeue(out request);
	}

	public void Dispose()
	{
		lock (_lock) _completed = true;
		_enqueued.Dispose();
	}
}
