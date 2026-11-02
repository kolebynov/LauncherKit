using R3;

namespace LauncherKit.Core.AppTasks.MicroTasks;

internal sealed class DelegateMicroTask : IAppMicroTask
{
	private readonly Func<ISubject<MicroTaskStep>, CancellationToken, ValueTask> _taskFunc;

	public MicroTaskDescription Description { get; }

	public DelegateMicroTask(MicroTaskDescription description, Func<ISubject<MicroTaskStep>, CancellationToken, ValueTask> taskFunc)
	{
		Description = description;
		_taskFunc = taskFunc;
	}

	public ValueTask Execute(ISubject<MicroTaskStep> stepSubject, CancellationToken cancellationToken) =>
		_taskFunc(stepSubject, cancellationToken);
}