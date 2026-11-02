﻿namespace LauncherKit.Core.Feed.Install;

public sealed class InstallStep
{
	public required IReadOnlyList<InstallAction> Actions { get; init; }

	public InstallCondition? Condition { get; init; }

	public bool CanRollback { get; init; }
}