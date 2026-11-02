﻿namespace LauncherKit.Core.Feed;

public union ActivateBetaCodeResult(ActivateBetaCodeResult.Success, ActivateBetaCodeResult.AlreadyActivated, ActivateBetaCodeResult.InvalidBetaCode)
{
	public sealed record Success;

	public sealed record AlreadyActivated;

	public sealed record InvalidBetaCode;
}