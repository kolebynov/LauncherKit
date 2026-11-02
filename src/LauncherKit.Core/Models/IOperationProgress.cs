using R3;
using LauncherKit.Core.AppTasks.MicroTasks;

namespace LauncherKit.Core.Models;

public interface IOperationProgress
{
	public MicroTaskDescription Operation { get; }

	public Observable<Unit> ProgressObservable { get; }

	public double Progress { get; }
}