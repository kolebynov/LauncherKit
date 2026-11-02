using LauncherKit.FeedEditor.Components.Shared;
using LauncherKit.FeedEditor.Feed;

namespace LauncherKit.FeedEditor.Components.Editors;

/// <summary>User-facing names and one-line summaries of mod install actions.</summary>
public static class ModInstallActionDisplay
{
	private const string InstallMod = "Установить мод";
	private const string SelectRequiredMod = "Выбрать обязательный мод";
	private const string ActivateMods = "Включить/отключить моды";
	private const string ActivatePlugins = "Включить/отключить плагины";
	private const string RemoveFileSystemItem = "Удалить файлы и папки";
	private const string ExtractArchive = "Распаковать архив";
	private const string MergePlugins = "Объединить список плагинов";
	private const string ChangeProfile = "Сменить профиль Mod Organizer";

	public static IReadOnlyList<ListItemFactory<ModInstallActionModel>> Factories { get; } =
	[
		new(InstallMod, () => new InstallModAction()),
		new(SelectRequiredMod, () => new SelectRequiredModAction()),
		new(ActivateMods, () => new ActivateModsAction()),
		new(ActivatePlugins, () => new ActivatePluginsAction()),
		new(RemoveFileSystemItem, () => new RemoveFileSystemItemAction()),
		new(ExtractArchive, () => new ExtractArchiveAction()),
		new(MergePlugins, () => new MergePluginsAction()),
		new(ChangeProfile, () => new ChangeProfileAction()),
	];

	public static string Summary(ModInstallActionModel action) => action switch
	{
		InstallModAction a => $"{InstallMod} {a.ModName}: {a.DownloadInfo.S3Path}",
		SelectRequiredModAction a => $"{SelectRequiredMod}: {string.Join(", ", a.Mods)}",
		ActivateModsAction a => $"{(a.Activate ? "Включить" : "Отключить")} моды: {string.Join(", ", a.Mods)}",
		ActivatePluginsAction a => $"{(a.Activate ? "Включить" : "Отключить")} плагины: {string.Join(", ", a.Plugins)}",
		RemoveFileSystemItemAction a => $"{RemoveFileSystemItem}: {string.Join(", ", a.Paths)}{(a.FailOnFailedDelete ? string.Empty : " (ошибки пропускаются)")}",
		ExtractArchiveAction a => $"{ExtractArchive}: {a.DownloadInfo.S3Path} → {a.DestinationPath}",
		MergePluginsAction a => $"{MergePlugins}: {a.DownloadInfo.S3Path}",
		ChangeProfileAction a => $"{ChangeProfile}: {a.ProfileName}",
		_ => throw new ArgumentException($"Unsupported mod install action type {action.GetType()}.", nameof(action)),
	};
}
