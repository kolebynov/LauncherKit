using R3;
using LauncherKit.Desktop.Services;

namespace LauncherKit.Desktop.Extensions;

public static class UiDispatcherExtensions
{
	public static Observable<T> ObserveOnUi<T>(this Observable<T> source, IUiDispatcher dispatcher) =>
		source.ObserveOn(new DispatcherSynchronizationContext(dispatcher));

	private sealed class DispatcherSynchronizationContext(IUiDispatcher dispatcher) : SynchronizationContext
	{
		public override void Post(SendOrPostCallback callback, object? state) =>
			dispatcher.Post(() => callback(state));
	}
}
