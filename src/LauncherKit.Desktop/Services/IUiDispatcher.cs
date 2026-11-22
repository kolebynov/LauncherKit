namespace LauncherKit.Desktop.Services;

/// <summary>Schedules state changes on the consumer's UI thread.</summary>
public interface IUiDispatcher
{
	void Post(Action action);

	Task InvokeAsync(Action action);

	Task<TResult> InvokeAsync<TResult>(Func<TResult> action);

	Task InvokeAsync(Func<Task> action);

	Task<TResult> InvokeAsync<TResult>(Func<Task<TResult>> action);
}
