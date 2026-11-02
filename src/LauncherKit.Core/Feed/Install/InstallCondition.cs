﻿namespace LauncherKit.Core.Feed.Install;

public sealed class InstallCondition
{
	public required int StepIndex { get; init; }

	public required string Key { get; init; }

	public required CompareOperator Operator { get; init; }

	public required string Value { get; init; }
}