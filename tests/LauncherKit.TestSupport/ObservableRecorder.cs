using R3;

namespace LauncherKit.TestSupport;

/// <summary>
/// Records values, completion and errors of an R3 <see cref="Observable{T}"/> for sequence assertions.
/// </summary>
public sealed class ObservableRecorder<T> : IDisposable
{
	private readonly List<T> _values = [];
	private readonly IDisposable _subscription;

	public ObservableRecorder(Observable<T> source)
	{
		ArgumentNullException.ThrowIfNull(source);
		_subscription = source.Subscribe(_values.Add, OnCompleted);
	}

	public IReadOnlyList<T> Values => _values;

	public Exception? Error { get; private set; }

	public bool IsCompleted { get; private set; }

	public void Dispose() => _subscription.Dispose();

	private void OnCompleted(Result result)
	{
		IsCompleted = true;
		Error = result.Exception;
	}
}
