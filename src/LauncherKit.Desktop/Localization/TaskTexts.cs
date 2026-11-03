using Microsoft.Extensions.Localization;
using LauncherKit.Core.AppTasks;

namespace LauncherKit.Desktop.Localization;

public sealed partial class TaskTexts(IStringLocalizer<DesktopStrings> strings)
{
	public LocalizedString Get(AppTaskDescription description) => description switch
	{
		AppTaskDescription.InstallModpack(var version) => strings[DesktopStrings.TaskInstallModpack, version],
		AppTaskDescription.InstallModpackPatch(var version) => strings[DesktopStrings.TaskInstallModpackPatch, version],
		AppTaskDescription.UninstallModpack => strings[DesktopStrings.TaskUninstallModpack],
		AppTaskDescription.InstallModpackComponent(_, var title) => strings[DesktopStrings.TaskInstallModpackComponent, title.Resolve()],
		AppTaskDescription.InstallMod(var modName) => strings[DesktopStrings.TaskInstallMod, modName],
		AppTaskDescription.UninstallMod(var modName) => strings[DesktopStrings.TaskUninstallMod, modName],
		AppTaskDescription.UpdateLauncher(var version) => strings[DesktopStrings.TaskUpdateLauncher, version],
		AppTaskDescription.RunGame(var name, _) => strings[DesktopStrings.TaskRunGame, name],
		AppTaskDescription.DeleteOldSaves => strings[DesktopStrings.TaskDeleteOldSaves],
		AppTaskDescription.InstallSkyrim => strings[DesktopStrings.TaskInstallSkyrim],
		AppTaskDescription.SelectModpackComponents => strings[DesktopStrings.TaskSelectModpackComponents],
		AppTaskDescription.InstallSevenZip => strings[DesktopStrings.TaskInstallSevenZip],
		AppTaskDescription.InstallVcRedist(var architecture) => strings[DesktopStrings.TaskInstallVcRedist, architecture],
		AppTaskDescription.CleanupOldData => strings[DesktopStrings.TaskCleanupOldData],
		AppTaskDescription.Custom(var text) => new LocalizedString(nameof(AppTaskDescription.Custom), text),
	};
}
