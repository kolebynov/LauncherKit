using Microsoft.Extensions.Localization;
using LauncherKit.Core.AppTasks.MicroTasks;

namespace LauncherKit.Desktop.Localization;

public sealed partial class TaskTexts
{
	public LocalizedString Get(MicroTaskDescription description) => description switch
	{
		MicroTaskDescription.Preparing => strings[DesktopStrings.MicroPreparing],
		MicroTaskDescription.DownloadFile(var fileName) => strings[DesktopStrings.MicroDownloadFile, fileName],
		MicroTaskDescription.ExtractArchive(var archiveName) => strings[DesktopStrings.MicroExtractArchive, archiveName],
		MicroTaskDescription.InstallMod(var modName) => strings[DesktopStrings.MicroInstallMod, modName],
		MicroTaskDescription.MergePlugins(var fileName) => strings[DesktopStrings.MicroMergePlugins, fileName],
		MicroTaskDescription.DeleteFiles => strings[DesktopStrings.MicroDeleteFiles],
		MicroTaskDescription.SelectRequiredMod => strings[DesktopStrings.MicroSelectRequiredMod],
		MicroTaskDescription.SelectModpackComponents => strings[DesktopStrings.MicroSelectModpackComponents],
		MicroTaskDescription.RunProcess(var fileName) => strings[DesktopStrings.MicroRunProcess, fileName],
		MicroTaskDescription.GameStarted(var name) => strings[DesktopStrings.MicroGameStarted, name],
		MicroTaskDescription.RunLauncherSetup => strings[DesktopStrings.MicroRunLauncherSetup],
		MicroTaskDescription.DeleteModOrganizer => strings[DesktopStrings.MicroDeleteModOrganizer],
		MicroTaskDescription.DeleteOldSaves => strings[DesktopStrings.MicroDeleteOldSaves],
		MicroTaskDescription.ExtractModpackPatch => strings[DesktopStrings.MicroExtractModpackPatch],
		MicroTaskDescription.UpdateModpackVersion => strings[DesktopStrings.MicroUpdateModpackVersion],
		MicroTaskDescription.DeletePreviousVersion => strings[DesktopStrings.MicroDeletePreviousVersion],
		MicroTaskDescription.DeleteUnneededFiles => strings[DesktopStrings.MicroDeleteUnneededFiles],
		MicroTaskDescription.ExtractModpack => strings[DesktopStrings.MicroExtractModpack],
		MicroTaskDescription.InstallPrerequisite(var id) => strings[DesktopStrings.MicroInstallPrerequisite, id],
		MicroTaskDescription.InstallSkyrim => strings[DesktopStrings.MicroInstallSkyrim],
		MicroTaskDescription.DeleteBackups => strings[DesktopStrings.MicroDeleteBackups],
		MicroTaskDescription.InstallSevenZip => strings[DesktopStrings.MicroInstallSevenZip],
		MicroTaskDescription.Installing => strings[DesktopStrings.MicroInstalling],
		MicroTaskDescription.PreparingInstallation => strings[DesktopStrings.MicroPreparingInstallation],
		MicroTaskDescription.FinishInstallation => strings[DesktopStrings.MicroFinishInstallation],
		MicroTaskDescription.ExtractModPatch(var version) => strings[DesktopStrings.MicroExtractModPatch, version],
		MicroTaskDescription.UninstallMod(var modName) => strings[DesktopStrings.MicroUninstallMod, modName],
		MicroTaskDescription.UpdateModMetadata => strings[DesktopStrings.MicroUpdateModMetadata],
		MicroTaskDescription.DeleteOldDownloads => strings[DesktopStrings.MicroDeleteOldDownloads],
		MicroTaskDescription.DeleteTemporaryFiles => strings[DesktopStrings.MicroDeleteTemporaryFiles],
		MicroTaskDescription.RestoreFile(var path) => strings[DesktopStrings.MicroRestoreFile, path],
		MicroTaskDescription.RestoreFiles => strings[DesktopStrings.MicroRestoreFiles],
		MicroTaskDescription.RevertArchiveExtraction(var archiveName) => strings[DesktopStrings.MicroRevertArchiveExtraction, archiveName],
		MicroTaskDescription.ChangeProfile(var profile) => strings[DesktopStrings.MicroChangeProfile, profile],
		MicroTaskDescription.RevertPluginsMerge(var fileName) => strings[DesktopStrings.MicroRevertPluginsMerge, fileName],
		MicroTaskDescription.DeleteFile(var path) => strings[DesktopStrings.MicroDeleteFile, path],
		MicroTaskDescription.BackupArchiveFiles(var archiveName) => strings[DesktopStrings.MicroBackupArchiveFiles, archiveName],
		MicroTaskDescription.CheckFileHash => strings[DesktopStrings.MicroCheckFileHash],
		MicroTaskDescription.PrepareFile => strings[DesktopStrings.MicroPrepareFile],
		MicroTaskDescription.ConfigureMod => strings[DesktopStrings.MicroConfigureMod],
		MicroTaskDescription.GetDownloadSizes => strings[DesktopStrings.MicroGetDownloadSizes],
		MicroTaskDescription.ActivatePlugins(var activate) => strings[activate ? DesktopStrings.ActivatePlugins : DesktopStrings.DeactivatePlugins],
		MicroTaskDescription.ActivateMods(var activate) => strings[activate ? DesktopStrings.ActivateMods : DesktopStrings.DeactivateMods],
		MicroTaskDescription.Custom(var value) => new LocalizedString(nameof(MicroTaskDescription.Custom), value),
	};
}
