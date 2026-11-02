﻿namespace LauncherKit.Core.AppTasks.CompleteVerifiers;

internal sealed class DelegateCompleteVerifier : IAppTaskCompleteVerifier
{
	private readonly Func<CancellationToken, ValueTask<bool>> _verifier;

	public DelegateCompleteVerifier(Func<CancellationToken, ValueTask<bool>> verifier)
	{
		_verifier = verifier;
	}

	public ValueTask<bool> IsTaskComplete(CancellationToken cancellationToken) => _verifier(cancellationToken);
}