﻿namespace LauncherKit.Core.Utils;

public interface IDiskSpaceChecker
{
	void CheckAvailableDiskSpace(string path, long size);
}
