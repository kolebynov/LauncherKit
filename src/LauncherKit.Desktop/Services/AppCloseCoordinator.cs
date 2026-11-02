using Microsoft.Extensions.Logging;
using LauncherKit.Desktop.Extensions;
using LauncherKit.Desktop.Services;

namespace LauncherKit.Desktop.Services;

public sealed class AppCloseCoordinator(Func<IEnumerable<IAppCloseHandler>> getHandlers, ILogger logger)
{
	private bool _isRunning;
	public bool IsClosing { get; private set; }

	public async ValueTask<bool> TryClose(AppCloseContext context)
	{
		if (IsClosing)
		{
			return true;
		}
		if (_isRunning)
		{
			return false;
		}

		_isRunning = true;
		try
		{
			foreach (var handler in getHandlers())
			{
				try
				{
					if (await handler.Handle(context) is IAppCloseHandler.HandleResult.CannotClose)
					{
						return false;
					}
				}
				catch (Exception exception)
				{
					logger.LogError(exception, "Error during app close handler invoking");
				}
			}
			IsClosing = true;
			return true;
		}
		finally
		{
			_isRunning = false;
		}
	}
}
