﻿using LauncherKit.Core.Exceptions;

namespace LauncherKit.Core.Utils;

public sealed class DiskSpaceChecker : IDiskSpaceChecker
{
	public void CheckAvailableDiskSpace(string path, long size)
	{
		var drive = DriveInfo.GetDrives().FirstOrDefault(x => x.Name == Path.GetPathRoot(path));
		if (drive == null)
		{
			return;
		}

		var requiredSpace = size + 8 * 1024 * 1024;
		if (drive.TotalFreeSpace < requiredSpace)
		{
			throw new NotEnoughSpaceException(drive.Name, requiredSpace, drive.TotalFreeSpace);
		}
	}
}
