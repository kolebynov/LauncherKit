using R3;
using LauncherKit.Core.AppTasks.MicroTasks;

namespace LauncherKit.Core.Models;

public sealed class DownloadProgress : IDownloadProgress, IDisposable
{
	private readonly Subject<Unit> _progressSubject = new();

	public long DownloadedBytes { get; private set; }

	public long TotalBytes { get; private set; }

	public double Speed { get; private set; }

	public bool IsPaused { get; private set; }

	public MicroTaskDescription Operation { get; }

	public DownloadProgress(string fileName)
	{
		Operation = new MicroTaskDescription.DownloadFile(fileName);
	}

	public Observable<Unit> ProgressObservable => _progressSubject;

	public double Progress => DownloadedBytes / (double)TotalBytes;

	public void ReportProgress(long downloadedBytes, long totalBytes, double speed)
	{
		DownloadedBytes = downloadedBytes;
		TotalBytes = totalBytes;
		Speed = speed;
		_progressSubject.OnNext(Unit.Default);
	}

	public ValueTask Pause()
	{
		if (IsPaused)
		{
			return ValueTask.CompletedTask;
		}

		IsPaused = true;
		_progressSubject.OnNext(Unit.Default);
		return ValueTask.CompletedTask;
	}

	public ValueTask Resume()
	{
		if (!IsPaused)
		{
			return ValueTask.CompletedTask;
		}

		IsPaused = false;
		_progressSubject.OnNext(Unit.Default);
		return ValueTask.CompletedTask;
	}

	public void Dispose()
	{
		_progressSubject.Dispose();
	}
}