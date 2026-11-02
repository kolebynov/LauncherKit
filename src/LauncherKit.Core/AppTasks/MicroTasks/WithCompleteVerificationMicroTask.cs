using R3;
using LauncherKit.Core.AppTasks.CompleteVerifiers;

namespace LauncherKit.Core.AppTasks.MicroTasks;

internal sealed class WithCompleteVerificationMicroTask : IAppMicroTask
{
	private readonly IAppMicroTask _microTask;
	private readonly IAppTaskCompleteVerifier _verifier;

	public MicroTaskDescription Description => _microTask.Description;

	public WithCompleteVerificationMicroTask(IAppMicroTask microTask, IAppTaskCompleteVerifier verifier)
	{
		_microTask = microTask;
		_verifier = verifier;
	}

	public async ValueTask Execute(ISubject<MicroTaskStep> stepSubject, CancellationToken cancellationToken)
	{
		if (!await _verifier.IsTaskComplete(cancellationToken))
		{
			await _microTask.Execute(stepSubject, cancellationToken);
		}
	}
}