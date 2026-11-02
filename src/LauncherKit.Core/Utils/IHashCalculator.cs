﻿using LauncherKit.Core.Models;

namespace LauncherKit.Core.Utils;

public interface IHashCalculator
{
	Task<Sha256Hash> HashFile(
		string filePath, Func<double, ValueTask>? progressTracker, CancellationToken cancellationToken);
}
