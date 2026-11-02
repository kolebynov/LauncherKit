using R3;
using LauncherKit.Core.AppTasks.MicroTasks;

namespace LauncherKit.Core.Models;

public sealed class OperationProgress : IOperationProgress, IDisposable
{
	private readonly Subject<Unit> _progressSubject = new();

	public MicroTaskDescription Operation { get; }

	public Observable<Unit> ProgressObservable => _progressSubject;

	public double Progress { get; private set; }

	public OperationProgress(MicroTaskDescription operation)
	{
		Operation = operation;
	}

	public void ReportProgress(double progress)
	{
		Progress = progress;
		_progressSubject.OnNext(Unit.Default);
	}

	public void Dispose()
	{
		_progressSubject.Dispose();
	}
}