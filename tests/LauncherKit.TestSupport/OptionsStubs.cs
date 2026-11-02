using Microsoft.Extensions.Options;

namespace LauncherKit.TestSupport;

public static class OptionsStubs
{
	/// <summary>
	/// Registers an <see cref="IOptions{TOptions}"/> mock on <paramref name="mocker"/>, so the system
	/// under test resolves it through <see cref="AutoMocker.CreateInstance{T}()"/>.
	/// </summary>
	public static IOptions<T> Options<T>(AutoMocker mocker, T value)
		where T : class
	{
		ArgumentNullException.ThrowIfNull(mocker);
		var mock = mocker.GetMock<IOptions<T>>();
		mock.Setup(x => x.Value).Returns(value);
		return mock.Object;
	}

	public static IOptionsSnapshot<T> Snapshot<T>(AutoMocker mocker, T value)
		where T : class
	{
		ArgumentNullException.ThrowIfNull(mocker);
		var mock = mocker.GetMock<IOptionsSnapshot<T>>();
		mock.Setup(x => x.Value).Returns(value);
		return mock.Object;
	}

	/// <summary>
	/// Hand-written monitor stub (no mock involved) whose value can be changed mid-test; registered on
	/// <paramref name="mocker"/> so the system under test observes the same instance.
	/// </summary>
	public static OptionsMonitorStub<T> Monitor<T>(AutoMocker mocker, T value)
		where T : class
	{
		ArgumentNullException.ThrowIfNull(mocker);
		var monitor = new OptionsMonitorStub<T>(value);
		mocker.Use<IOptionsMonitor<T>>(monitor);
		return monitor;
	}
}

/// <summary>
/// <see cref="IOptionsMonitor{TOptions}"/> whose value can be changed from a test to exercise
/// change-reaction behavior.
/// </summary>
public sealed class OptionsMonitorStub<T> : IOptionsMonitor<T>
	where T : class
{
	private readonly List<Action<T, string?>> _listeners = [];

	public OptionsMonitorStub(T value)
	{
		CurrentValue = value;
	}

	public T CurrentValue { get; private set; }

	public T Get(string? name) => CurrentValue;

	public IDisposable OnChange(Action<T, string?> listener)
	{
		_listeners.Add(listener);
		return new Subscription(this, listener);
	}

	public void Change(T newValue)
	{
		CurrentValue = newValue;
		foreach (var listener in _listeners.ToArray())
		{
			listener(newValue, null);
		}
	}

	private sealed class Subscription : IDisposable
	{
		private readonly OptionsMonitorStub<T> _monitor;
		private readonly Action<T, string?> _listener;

		public Subscription(OptionsMonitorStub<T> monitor, Action<T, string?> listener)
		{
			_monitor = monitor;
			_listener = listener;
		}

		public void Dispose() => _monitor._listeners.Remove(_listener);
	}
}
