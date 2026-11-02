using System.Diagnostics;

namespace LauncherKit.Core.Services.Abstractions;

public interface IProcessRunner
{
	Task<int> StartProcess(ProcessStartInfo startInfo, CancellationToken cancellationToken,
		Action<string?>? outputProcessor = null);

	Task StartProcessAndAssertSuccess(ProcessStartInfo startInfo, bool assert, CancellationToken cancellationToken,
		Action<string?>? outputProcessor = null);
}
