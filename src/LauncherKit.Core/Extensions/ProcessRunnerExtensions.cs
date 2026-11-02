﻿using System.Diagnostics;
using LauncherKit.Core.Services.Abstractions;

namespace LauncherKit.Core.Extensions;

public static class ProcessRunnerExtensions
{
	public static void OpenDirectory(this IProcessRunner processRunner, string directory)
	{
		processRunner.StartProcess(
			new ProcessStartInfo(directory) { UseShellExecute = true },
			CancellationToken.None).Forget();
	}
}