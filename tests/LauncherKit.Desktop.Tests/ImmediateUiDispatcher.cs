using LauncherKit.Desktop.Services;

namespace LauncherKit.Desktop.Tests;

internal sealed class ImmediateUiDispatcher : IUiDispatcher
{
	public void Post(Action action) => action();

	public Task InvokeAsync(Action action)
	{
		try
		{
			action();
			return Task.CompletedTask;
		}
		catch (Exception e)
		{
			return Task.FromException(e);
		}
	}

	public Task InvokeAsync(Func<Task> func)
	{
		try
		{
			return func();
		}
		catch (Exception e)
		{
			return Task.FromException(e);
		}
	}

	public Task<TResult> InvokeAsync<TResult>(Func<TResult> action)
	{
		try
		{
			return Task.FromResult(action());
		}
		catch (Exception e)
		{
			return Task.FromException<TResult>(e);
		}
	}

	public Task<TResult> InvokeAsync<TResult>(Func<Task<TResult>> action)
	{
		try
		{
			return action();
		}
		catch (Exception e)
		{
			return Task.FromException<TResult>(e);
		}
	}
}
