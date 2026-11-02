using R3;

namespace LauncherKit.Core.AppTasks.MicroTasks;

public interface IAppMicroTask
{
	MicroTaskDescription Description { get; }

	ValueTask Execute(ISubject<MicroTaskStep> stepSubject, CancellationToken cancellationToken);
}