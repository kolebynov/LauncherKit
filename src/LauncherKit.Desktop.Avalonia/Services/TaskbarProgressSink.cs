using Avalonia.Controls;
using LauncherKit.Desktop.Avalonia.Interop;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;

namespace LauncherKit.Desktop.Avalonia.Services;

internal sealed class TaskbarProgressSink : ITaskbarProgressSink
{
	private readonly Func<Window?> _mainWindowProvider;
	private readonly Func<ITaskbarList3> _taskbarListFactory;
	private readonly ILogger<TaskbarProgressSink> _logger;
	private ITaskbarList3? _taskbarList3;
	private bool _taskbarListUnavailable;

	/// <param name="taskbarListFactory">
	/// Creates the COM object; called on the UI thread, so the object lives in the UI thread's apartment.
	/// </param>
	public TaskbarProgressSink(Func<Window?> mainWindowProvider, Func<ITaskbarList3> taskbarListFactory,
		ILogger<TaskbarProgressSink> logger)
	{
		_mainWindowProvider = mainWindowProvider;
		_taskbarListFactory = taskbarListFactory;
		_logger = logger;
	}

	public void SetProgress(TaskbarProgressBarState state, ulong completed, ulong total)
	{
		Dispatcher.UIThread.Post(() =>
		{
			var handle = _mainWindowProvider()?.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
			if (handle == IntPtr.Zero || GetTaskbarList() is not { } taskbarList3)
			{
				return;
			}

			try
			{
				taskbarList3.SetProgressState(handle, state);
				if (state is TaskbarProgressBarState.Normal or TaskbarProgressBarState.Error)
				{
					taskbarList3.SetProgressValue(handle, completed, total);
				}
			}
			catch (Exception ex)
			{
				_logger.LogDebug(ex, "Failed to update taskbar progress.");
			}
		});
	}

	private ITaskbarList3? GetTaskbarList()
	{
		if (_taskbarList3 != null || _taskbarListUnavailable)
		{
			return _taskbarList3;
		}

		try
		{
			_taskbarList3 = _taskbarListFactory();
		}
		catch (Exception ex)
		{
			_taskbarListUnavailable = true;
			_logger.LogWarning(ex, "Failed to create the taskbar list; taskbar progress is disabled.");
		}

		return _taskbarList3;
	}
}
