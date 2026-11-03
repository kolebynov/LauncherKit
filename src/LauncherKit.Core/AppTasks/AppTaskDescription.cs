using NuGet.Versioning;
using LauncherKit.Core.Feed;

namespace LauncherKit.Core.AppTasks;

public union AppTaskDescription(
	AppTaskDescription.InstallModpack,
	AppTaskDescription.InstallModpackPatch,
	AppTaskDescription.UninstallModpack,
	AppTaskDescription.InstallModpackComponent,
	AppTaskDescription.InstallMod,
	AppTaskDescription.UninstallMod,
	AppTaskDescription.UpdateLauncher,
	AppTaskDescription.RunGame,
	AppTaskDescription.DeleteOldSaves,
	AppTaskDescription.InstallSkyrim,
	AppTaskDescription.SelectModpackComponents,
	AppTaskDescription.InstallSevenZip,
	AppTaskDescription.InstallVcRedist,
	AppTaskDescription.CleanupOldData,
	AppTaskDescription.Custom)
{
	public sealed record InstallModpack(NuGetVersion Version);

	public sealed record InstallModpackPatch(NuGetVersion Version);

	public sealed record UninstallModpack;

	public sealed record InstallModpackComponent(string Id, LocalizedText Title);

	public sealed record InstallMod(string ModName);

	public sealed record UninstallMod(string ModName);

	public sealed record UpdateLauncher(NuGetVersion Version);

	public sealed record RunGame(StartGameVariant Variant);

	public sealed record DeleteOldSaves;

	public sealed record InstallSkyrim;

	public sealed record SelectModpackComponents;

	public sealed record InstallSevenZip;

	public sealed record InstallVcRedist(string Architecture);

	public sealed record CleanupOldData;

	public sealed record Custom(string Text);
}
