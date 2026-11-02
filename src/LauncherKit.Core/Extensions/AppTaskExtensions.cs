﻿using LauncherKit.Core.AppTasks.CompleteVerifiers;
using LauncherKit.Core.AppTasks.MicroTasks;

namespace LauncherKit.Core.Extensions;

public static class AppTaskExtensions
{
	public static IAppMicroTask WithCompleteVerification(
		this IAppMicroTask appMicroTask, IAppTaskCompleteVerifier verifier) =>
		new WithCompleteVerificationMicroTask(appMicroTask, verifier);
}