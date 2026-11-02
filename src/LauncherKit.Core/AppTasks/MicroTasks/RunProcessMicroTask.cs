using System.Diagnostics;
using R3;
using LauncherKit.Core.Services.Abstractions;

namespace LauncherKit.Core.AppTasks.MicroTasks;

internal sealed class RunProcessMicroTask : IAppMicroTask
{
	private readonly IProcessRunner _processRunner;
	private readonly Func<ProcessStartInfo> _processStartInfoProvider;
	private readonly bool _throwForNonZeroExitCode;

	public MicroTaskDescription Description { get; }

	public RunProcessMicroTask(MicroTaskDescription description, IProcessRunner processRunner, Func<ProcessStartInfo> processStartInfoProvider,
		bool throwForNonZeroExitCode)
	{
		Description = description;
		_processRunner = processRunner;
		_processStartInfoProvider = processStartInfoProvider;
		_throwForNonZeroExitCode = throwForNonZeroExitCode;
	}

	public async ValueTask Execute(ISubject<MicroTaskStep> stepSubject, CancellationToken cancellationToken)
	{
		var processStartInfo = _processStartInfoProvider();
		var exitCode = await _processRunner.StartProcess(processStartInfo, cancellationToken);

		if (exitCode != 0 && _throwForNonZeroExitCode)
		{
			throw new InvalidOperationException($"Process {processStartInfo.FileName} exited with code {exitCode}");
		}
	}
}