using Avalonia.Threading;

namespace LauncherKit.Desktop.Services;

public sealed class AvaloniaUiDispatcher : IUiDispatcher
{
	public void Post(Action action) => Dispatcher.UIThread.Post(action);

	public Task InvokeAsync(Action action) => Dispatcher.UIThread.InvokeAsync(action).GetTask();

	public Task<TResult> InvokeAsync<TResult>(Func<TResult> action) => Dispatcher.UIThread.InvokeAsync(action).GetTask();

	public Task InvokeAsync(Func<Task> action) => Dispatcher.UIThread.InvokeAsync(action);

	public Task<TResult> InvokeAsync<TResult>(Func<Task<TResult>> action) => Dispatcher.UIThread.InvokeAsync(action);
}
