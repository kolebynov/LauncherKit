﻿namespace LauncherKit.Core.Update;

public union UpdateType(UpdateType.InitialInstall, UpdateType.MajorUpdate, UpdateType.PatchUpdate)
{
	public sealed record InitialInstall;

	public sealed record MajorUpdate;

	public sealed record PatchUpdate(bool RequireNewGame);
}