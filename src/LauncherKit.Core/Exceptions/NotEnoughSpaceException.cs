﻿namespace LauncherKit.Core.Exceptions;

#pragma warning disable CA1032
public sealed class NotEnoughSpaceException : Exception
#pragma warning restore CA1032
{
	public string DiskName { get; }

	public long RequiredSpace { get; }

	public long AvailableFreeSpace { get; }

	public override string Message => $"Not enough space on {DiskName} ({AvailableFreeSpace} bytes available, {RequiredSpace} bytes required)";

	public NotEnoughSpaceException(string diskName, long requiredSpace, long availableFreeSpace)
	{
		DiskName = diskName;
		RequiredSpace = requiredSpace;
		AvailableFreeSpace = availableFreeSpace;
	}
}