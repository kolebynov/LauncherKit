using LauncherKit.Desktop.Services;

namespace LauncherKit.Desktop.Tests;

/// <summary>Queues every dispatched action until <see cref="Drain"/> runs them in order, including actions they post.</summary>
internal sealed class QueuedUiDispatcher : IUiDispatcher
{
	private readonly Queue<Action> _actions = new();

	public void Post(Action action) => _actions.Enqueue(action);

	public Task InvokeAsync(Action action) => InvokeAsync(() =>
	{
		action();
		return Task.CompletedTask;
	});

	public Task InvokeAsync(Func<Task> func)
	{
		var completion = new TaskCompletionSource();
		Post(async () =>
		{
			try
			{
				await func();
				completion.SetResult();
			}
			catch (Exception e)
			{
				completion.SetException(e);
			}
		});
		return completion.Task;
	}

	public Task<TResult> InvokeAsync<TResult>(Func<TResult> action) =>
		InvokeAsync<TResult>(() => Task.FromResult(action()));

	public Task<TResult> InvokeAsync<TResult>(Func<Task<TResult>> action)
	{
		var completion = new TaskCompletionSource<TResult>();
		Post(async () =>
		{
			try
			{
				completion.SetResult(await action());
			}
			catch (Exception e)
			{
				completion.SetException(e);
			}
		});
		return completion.Task;
	}

	public void Drain()
	{
		while (_actions.TryDequeue(out var action)) action();
	}
}
