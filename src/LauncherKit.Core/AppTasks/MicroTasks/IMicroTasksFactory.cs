using System.Diagnostics;
using R3;
using LauncherKit.Core.AppTasks.ModpackComponents;
using LauncherKit.Core.Backups;
using LauncherKit.Core.Feed.Mods;
using LauncherKit.Core.Models;

namespace LauncherKit.Core.AppTasks.MicroTasks;

public interface IMicroTasksFactory
{
	IAppMicroTask CreateDelegateMicroTask(
		Func<ISubject<MicroTaskStep>, CancellationToken, ValueTask> taskFunc, MicroTaskDescription description);

	IAppMicroTask CreateDeleteFileSystemItemsMicroTask(IReadOnlyCollection<SkyrimRelativePath> paths,
		bool failOnFailedDelete, MicroTaskDescription description);

	IAppMicroTask CreateDownloadFileMicroTask(DownloadFileSource downloadFileSource);

	IAppMicroTask CreateExtractSevenZipMicroTask(string archiveName, SkyrimRelativePath destinationPath,
		MicroTaskDescription? description = null);

	/// <summary>Extracts the whole archive except <paramref name="excludedEntryPath"/>, its path in the archive.</summary>
	IAppMicroTask CreateExtractSevenZipExceptMicroTask(string archiveName, SkyrimRelativePath destinationPath,
		string excludedEntryPath, MicroTaskDescription description);

	/// <summary>Extracts only <paramref name="entryPath"/>, keeping its path, when the archive contains it.</summary>
	IAppMicroTask CreateExtractSevenZipEntryMicroTask(string archiveName, SkyrimRelativePath destinationPath,
		string entryPath, MicroTaskDescription description);

	IAppMicroTask CreateExtractZipMicroTask(string archiveName, string destinationDirectory, MicroTaskDescription description);

	/// <summary>Installs the downloaded archive as the mod, at the placement in the mod list when set.</summary>
	IAppMicroTask CreateInstallModMicroTask(string modName, string archiveName, ModPlacement? placement);

	/// <summary>
	/// Merges the downloaded <c>plugins.txt</c> into the selected profile's plugin lists, keeping what the merge needs
	/// to be undone in <paramref name="backup"/> when it is not <see langword="null"/>.
	/// </summary>
	IAppMicroTask CreateMergePluginsMicroTask(string pluginsFileName, InstallActionBackup? backup);

	IAppMicroTask CreateRunProcessMicroTask(Func<ProcessStartInfo> processStartInfoProvider,
		bool throwForNonZeroExitCode, MicroTaskDescription description);

	IAppMicroTask CreateSelectRequiredModMicroTask(InstallStepContext context, IReadOnlyList<string> requiredMods);

	IAppMicroTask CreateSelectModpackComponentsMicroTask(ModpackComponentsOffer offer);
}
