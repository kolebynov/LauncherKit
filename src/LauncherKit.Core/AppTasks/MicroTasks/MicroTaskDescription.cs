using NuGet.Versioning;

namespace LauncherKit.Core.AppTasks.MicroTasks;

public union MicroTaskDescription(
	MicroTaskDescription.Preparing,
	MicroTaskDescription.DownloadFile,
	MicroTaskDescription.ExtractArchive,
	MicroTaskDescription.InstallMod,
	MicroTaskDescription.MergePlugins,
	MicroTaskDescription.DeleteFiles,
	MicroTaskDescription.SelectRequiredMod,
	MicroTaskDescription.SelectModpackComponents,
	MicroTaskDescription.RunProcess,
	MicroTaskDescription.GameStarted,
	MicroTaskDescription.RunLauncherSetup,
	MicroTaskDescription.DeleteModOrganizer,
	MicroTaskDescription.DeleteOldSaves,
	MicroTaskDescription.ExtractModpackPatch,
	MicroTaskDescription.UpdateModpackVersion,
	MicroTaskDescription.DeletePreviousVersion,
	MicroTaskDescription.DeleteUnneededFiles,
	MicroTaskDescription.ExtractModpack,
	MicroTaskDescription.InstallPrerequisite,
	MicroTaskDescription.InstallSkyrim,
	MicroTaskDescription.DeleteBackups,
	MicroTaskDescription.InstallSevenZip,
	MicroTaskDescription.Installing,
	MicroTaskDescription.PreparingInstallation,
	MicroTaskDescription.FinishInstallation,
	MicroTaskDescription.ExtractModPatch,
	MicroTaskDescription.UninstallMod,
	MicroTaskDescription.UpdateModMetadata,
	MicroTaskDescription.DeleteOldDownloads,
	MicroTaskDescription.DeleteTemporaryFiles,
	MicroTaskDescription.ActivatePlugins,
	MicroTaskDescription.RestoreFile,
	MicroTaskDescription.RestoreFiles,
	MicroTaskDescription.RevertArchiveExtraction,
	MicroTaskDescription.ChangeProfile,
	MicroTaskDescription.RevertPluginsMerge,
	MicroTaskDescription.DeleteFile,
	MicroTaskDescription.BackupArchiveFiles,
	MicroTaskDescription.ActivateMods,
	MicroTaskDescription.CheckFileHash,
	MicroTaskDescription.PrepareFile,
	MicroTaskDescription.ConfigureMod,
	MicroTaskDescription.GetDownloadSizes,
	MicroTaskDescription.Custom)
{
	public sealed record Preparing;

	public sealed record DownloadFile(string FileName);

	public sealed record ExtractArchive(string ArchiveName);

	public sealed record InstallMod(string ModName);

	public sealed record MergePlugins(string FileName);

	public sealed record DeleteFiles;

	public sealed record SelectRequiredMod;

	public sealed record SelectModpackComponents;

	public sealed record RunProcess(string FileName);

	public sealed record GameStarted(string Name);

	public sealed record RunLauncherSetup;

	public sealed record DeleteModOrganizer;

	public sealed record DeleteOldSaves;

	public sealed record ExtractModpackPatch;

	public sealed record UpdateModpackVersion;

	public sealed record DeletePreviousVersion;

	public sealed record DeleteUnneededFiles;

	public sealed record ExtractModpack;

	public sealed record InstallPrerequisite(string Id);

	public sealed record InstallSkyrim;

	public sealed record DeleteBackups;

	public sealed record InstallSevenZip;

	public sealed record Installing;

	public sealed record PreparingInstallation;

	public sealed record FinishInstallation;

	public sealed record ExtractModPatch(NuGetVersion Version);

	public sealed record UninstallMod(string ModName);

	public sealed record UpdateModMetadata;

	public sealed record DeleteOldDownloads;

	public sealed record DeleteTemporaryFiles;

	public sealed record ActivatePlugins(bool Activate);

	public sealed record RestoreFile(string Path);

	public sealed record RestoreFiles;

	public sealed record RevertArchiveExtraction(string ArchiveName);

	public sealed record ChangeProfile(string Profile);

	public sealed record RevertPluginsMerge(string FileName);

	public sealed record DeleteFile(string Path);

	public sealed record BackupArchiveFiles(string ArchiveName);

	public sealed record ActivateMods(bool Activate);

	public sealed record CheckFileHash;

	public sealed record PrepareFile;

	public sealed record ConfigureMod;

	public sealed record GetDownloadSizes;

	public sealed record Custom(string Text);
}
