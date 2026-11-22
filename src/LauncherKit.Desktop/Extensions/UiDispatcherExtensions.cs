using System.Runtime.ExceptionServices;
using R3;
using LauncherKit.Desktop.Services;

namespace LauncherKit.Desktop.Extensions;

public static class UiDispatcherExtensions
{
	public static Observable<T> ObserveOnUi<T>(this Observable<T> source, IUiDispatcher dispatcher) =>
		source.ObserveOn(new DispatcherSynchronizationContext(dispatcher));

	public static void PostException(this IUiDispatcher dispatcher, Exception exception)
	{
		dispatcher.Post(() => ExceptionDispatchInfo.Throw(exception));
	}

	private sealed class DispatcherSynchronizationContext(IUiDispatcher dispatcher) : SynchronizationContext
	{
		public override void Post(SendOrPostCallback callback, object? state) =>
			dispatcher.Post(() => callback(state));
	}
}
