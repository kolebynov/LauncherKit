﻿using LauncherKit.Infrastructure;

namespace LauncherKit.Core.Extensions;

public static class LaunchersRootDirectoryManagerExtensions
{
	extension(ILaunchersRootDirectoryManager launchersRootDirectoryManager)
	{
		public string ToolsDirectory => Path.Combine(launchersRootDirectoryManager.Directory, "Tools");

		public string SevenZipDirectory => Path.Combine(launchersRootDirectoryManager.ToolsDirectory, "7-Zip");

		public string SevenZipExePath => Path.Combine(launchersRootDirectoryManager.SevenZipDirectory, "7z.exe");

		public string SevenZipGExePath => Path.Combine(launchersRootDirectoryManager.SevenZipDirectory, "7zG.exe");
	}
}