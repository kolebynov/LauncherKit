﻿namespace LauncherKit.Core.AppTasks.CompleteVerifiers;

public interface IAppTaskCompleteVerifier
{
	ValueTask<bool> IsTaskComplete(CancellationToken cancellationToken);
}