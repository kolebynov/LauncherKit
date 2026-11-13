using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NuGet.Versioning;
using LauncherKit.Core.AppTasks.CompleteVerifiers;
using LauncherKit.Core.AppTasks.InstallSteps;
using LauncherKit.Core.AppTasks.MicroTasks;
using LauncherKit.Core.AppTasks.ModpackComponents;
using LauncherKit.Core.Backups;
using LauncherKit.Core.Extensions;
using LauncherKit.Core.Feed;
using LauncherKit.Core.Feed.Mods;
using LauncherKit.Core.Feed.Modpacks;
using LauncherKit.Core.FileStorage;
using LauncherKit.Core.Models;
using LauncherKit.Core.Services.Abstractions;
using LauncherKit.Core.Utils;
using LauncherKit.Infrastructure;

namespace LauncherKit.Core.AppTasks;

public sealed class AppTaskProvider : IAppTaskProvider
{
	public const string SelectModpackComponentsTaskId = "SelectModpackComponents";
	public const string InstallModpackTaskId = "InstallModpack";
	public const string InstallModpackPatchTaskIdPrefix = "InstallModpackPatch_";
	public const string InstallModpackComponentTaskIdPrefix = "InstallModpackComponent_";
	public const string UninstallModpackTaskId = "UninstallModpack";

	private readonly AppSettings _appSettings;
	private readonly LauncherProductOptions _product;
	private readonly IApplicationLifetime _applicationLifetime;
	private readonly IFeedProvider _feedProvider;
	private readonly IFileStorageService _fileStorageService;
	private readonly IInstallAppTaskProvider _installAppTaskProvider;
	private readonly IModOrganizerService _modOrganizerService;
	private readonly IPrerequisitesAppTaskProvider _prerequisitesAppTaskProvider;
	private readonly IProcessRunner _processRunner;
	private readonly ISkyrimInstallationChecker _skyrimInstallationChecker;
	private readonly IModpackVersionProvider _modpackVersionProvider;

	public AppTaskProvider(IOptionsSnapshot<AppSettings> appSettings, IApplicationLifetime applicationLifetime,
		IFeedProvider feedProvider, IFileStorageService fileStorageService, IInstallAppTaskProvider installAppTaskProvider,
		IModOrganizerService modOrganizerService, IPrerequisitesAppTaskProvider prerequisitesAppTaskProvider,
		IProcessRunner processRunner, ISkyrimInstallationChecker skyrimInstallationChecker,
		IModpackVersionProvider modpackVersionProvider, IOptions<LauncherProductOptions> product)
	{
		_appSettings = appSettings.Value;
		_product = product.Value;
		_applicationLifetime = applicationLifetime;
		_feedProvider = feedProvider;
		_fileStorageService = fileStorageService;
		_installAppTaskProvider = installAppTaskProvider;
		_modOrganizerService = modOrganizerService;
		_prerequisitesAppTaskProvider = prerequisitesAppTaskProvider;
		_processRunner = processRunner;
		_skyrimInstallationChecker = skyrimInstallationChecker;
		_modpackVersionProvider = modpackVersionProvider;
	}

	/// <summary>
	/// Whether <paramref name="taskId"/> changes the modpack installation: a task of the installation chain (component
	/// choice, modpack, patches, components, graph pack), which keeps changing the installation until its last task
	/// finishes, or the modpack removal.
	/// </summary>
	public static bool IsModpackInstallationTask(string taskId) =>
		taskId is SelectModpackComponentsTaskId or InstallModpackTaskId or UninstallModpackTaskId ||
		taskId.StartsWith(InstallModpackPatchTaskIdPrefix, StringComparison.Ordinal) ||
		taskId.StartsWith(InstallModpackComponentTaskIdPrefix, StringComparison.Ordinal);

	public async ValueTask<AppTask> CreateRunGameTask(GameLaunch launch, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(launch);
		var microTaskDescription = new MicroTaskDescription.GameStarted(launch.Name);
		// The feed may offer no modpack to this user (e.g. only beta versions gated by a code the user has not activated);
		// the game then runs on whatever is installed.
		var newestModpackFeedItem = (await _feedProvider.GetModpackFeedItems(cancellationToken)).MaxBy(x => x.Version);

		return new AppTask(
			"RunGame", new AppTaskDescription.RunGame(launch.Name, launch.ProgramPath),
			context =>
			[
				context.MicroTasksFactory.CreateDelegateMicroTask(
					(_, ct) => _modOrganizerService.StartModOrganizer(launch.ProgramPath, null, false, ct).AsValueTask(),
					microTaskDescription)
			],
			newestModpackFeedItem != null
				? [await CreateInstallModpackTaskInternal(newestModpackFeedItem, [], false, cancellationToken)]
				: [],
			null);
	}

	public AppTask CreateUpdateLauncherTask(DownloadInfo downloadInfo, NuGetVersion version) =>
		new(
			"UpdateLauncher", new AppTaskDescription.UpdateLauncher(version),
			context =>
			[
				context.MicroTasksFactory.CreateDownloadFileMicroTask(downloadInfo),
				context.MicroTasksFactory.CreateDelegateMicroTask(
					(_, _) =>
					{
						// The setup is a per-user bundle: it needs no elevation (an elevated one run by another admin
						// account would install into that account's profile), shows only its progress, and the
						// launcher's MSI starts the updated launcher once installed.
						var filePath = _fileStorageService.GetFilePath(downloadInfo.FileName)!;
						_processRunner.StartProcess(
							new ProcessStartInfo(filePath, "/passive")
							{
								WorkingDirectory = Path.GetDirectoryName(filePath)!,
								UseShellExecute = true,
							},
							CancellationToken.None).Forget();
						_applicationLifetime.Exit();
						return ValueTask.CompletedTask;
					}, MicroTaskDescription.RunLauncherSetup.Instance),
			], [], null);

	public ValueTask<AppTask> CreateInstallModpackTask(ModpackFeedItem modpackFeedItem, IReadOnlyList<ModpackPatchFeedItem> patches,
		CancellationToken cancellationToken) =>
		CreateInstallModpackTaskInternal(modpackFeedItem, patches, true, cancellationToken);

	public AppTask CreateUninstallModpackTask() =>
		new(
			UninstallModpackTaskId, AppTaskDescription.UninstallModpack.Instance,
			context =>
			[
				context.MicroTasksFactory.CreateDeleteFileSystemItemsMicroTask([new SkyrimRelativePath(_product.ModOrganizerDirectoryName)], true,
					MicroTaskDescription.DeleteModOrganizer.Instance),
				CreateDeleteAllBackupsMicroTask(context),
			], [], null);

	public AppTask CreateDeleteOldSavesTask(int savesToKeep, Action<DeletedSavesInfo> onDeleted) =>
		new(
			"DeleteOldSaves", AppTaskDescription.DeleteOldSaves.Instance,
			context =>
			[
				context.MicroTasksFactory.CreateDelegateMicroTask(
					async (_, ct) => onDeleted(await _modOrganizerService.DeleteOldSaves(savesToKeep, ct)),
					MicroTaskDescription.DeleteOldSaves.Instance),
			], [], null);

	public ValueTask<AppTask> CreateInstallPatchTask(
		ModpackPatchFeedItem modpackPatchFeedItem, bool forceUpdate, CancellationToken cancellationToken) =>
		CreateInstallPatchTaskInternal(modpackPatchFeedItem, forceUpdate, [], cancellationToken);

	/// <summary>
	/// Downloads the patch, extracts it without the modpack version file, runs its install steps, and only then extracts the
	/// version file. The installed modpack version is read from that file, so a patch whose steps failed or were interrupted
	/// is still offered (and its steps retried) after a restart, instead of counting as installed.
	/// </summary>
	private async ValueTask<AppTask> CreateInstallPatchTaskInternal(ModpackPatchFeedItem modpackPatchFeedItem,
		bool forceUpdate, IReadOnlyList<AppTask> precedingTasks, CancellationToken cancellationToken)
	{
		var archiveName = modpackPatchFeedItem.DownloadInfo.FileName;
		var versionFileEntry = new SkyrimRelativePath(_product.ModpackVersionFile).Path;
		return new AppTask(
			$"{InstallModpackPatchTaskIdPrefix}{modpackPatchFeedItem.Version}", new AppTaskDescription.InstallModpackPatch(modpackPatchFeedItem.Version),
			context =>
			[
				context.MicroTasksFactory.CreateDownloadFileMicroTask(modpackPatchFeedItem.DownloadInfo),
				context.MicroTasksFactory.CreateExtractSevenZipExceptMicroTask(
					archiveName, SkyrimRelativePath.Root, versionFileEntry, MicroTaskDescription.ExtractModpackPatch.Instance),
				// A patch is never rolled back, so its steps take no backups.
				..context.ServiceProvider.GetRequiredService<IInstallStepsMicroTasksFactory>()
					.CreateInstallMicroTasks(modpackPatchFeedItem.InstallSteps ?? [], null),
				context.MicroTasksFactory.CreateExtractSevenZipEntryMicroTask(
					archiveName, SkyrimRelativePath.Root, versionFileEntry, MicroTaskDescription.UpdateModpackVersion.Instance),
			],
			[
				..precedingTasks,
				await CreateInstallSkyrimTask(cancellationToken),
				await _prerequisitesAppTaskProvider.CreateInstallSevenZipTask(cancellationToken),
			],
			new DelegateCompleteVerifier(_ => ValueTask.FromResult(!forceUpdate && _modpackVersionProvider.InstalledModpackVersion is { } ver && ver >= modpackPatchFeedItem.Version)));
	}

	/// <summary>Offers supported feed components before installation and installs accepted choices in feed order.</summary>
	private async ValueTask<AppTask> CreateInstallModpackTaskInternal(ModpackFeedItem modpackFeedItem,
		IReadOnlyList<ModpackPatchFeedItem> patches, bool forceUpdate, CancellationToken cancellationToken)
	{
		var installedVerifier = new DelegateCompleteVerifier(_ =>
			ValueTask.FromResult(!forceUpdate && _modpackVersionProvider.InstalledModpackVersion != null));
		var selection = new ModpackComponentsSelection();
		var finalModpackVersion = patches.Select(x => x.Version).Append(modpackFeedItem.Version).Max()!;
		var offered = new List<ModpackComponentOffer>();
		var mods = await _feedProvider.GetModFeedDatas(cancellationToken);
		foreach (var definition in (await _feedProvider.GetInstallComponents(cancellationToken))
			.Where(x => x.SupportedModpackVersion.Satisfies(finalModpackVersion)).OrderBy(x => x.Order))
		{
			IReadOnlyList<ModComponentOption> options;
			if (definition.Kind == InstallComponentKind.ModChoice)
			{
				options = await CreateModChoiceOptions(definition.Options ?? [], mods, finalModpackVersion, cancellationToken);
			}
			else
			{
				var mod = mods.FirstOrDefault(x => x.Name == definition.ModName);
				if (mod?.FindLatestSupportedItem(finalModpackVersion) is not { } item)
				{
					continue;
				}
				var task = await _installAppTaskProvider.CreateModInstallAppTask(mod, item.Version, cancellationToken);
				options = [new ModComponentOption(definition.Title.Resolve(), null, task, item.GetDownloads().ToArray())];
			}
			if (options.Count > 0)
			{
				offered.Add(new ModpackComponentOffer(definition, options));
			}
		}
		var componentsOffer = new ModpackComponentsOffer(offered, selection);
		AppTask[] selectComponentsTasks = offered.Count > 0
			? [new AppTask(SelectModpackComponentsTaskId, AppTaskDescription.SelectModpackComponents.Instance,
				context => [context.MicroTasksFactory.CreateSelectModpackComponentsMicroTask(componentsOffer)], [], installedVerifier)]
			: [];
		var prerequisites = await _feedProvider.GetPrerequisites(cancellationToken);
		var lastTask = new AppTask(
			InstallModpackTaskId, new AppTaskDescription.InstallModpack(modpackFeedItem.Version),
			context =>
			{
				var microTasks = new List<IAppMicroTask>(5)
				{
					context.MicroTasksFactory.CreateDownloadFileMicroTask(modpackFeedItem.DownloadInfo),
					context.MicroTasksFactory.CreateDeleteFileSystemItemsMicroTask([new SkyrimRelativePath(_product.ModOrganizerDirectoryName)], true,
						MicroTaskDescription.DeletePreviousVersion.Instance),
					CreateDeleteAllBackupsMicroTask(context),
				};

				if (modpackFeedItem.Version.Major >= 3)
				{
					SkyrimRelativePath[] pathsToDelete =
					[
						new("resources"), new("AppCore.dll"), new("Ultralight.dll"), new("UltralightCore.dll"),
						new("WebCore.dll"),
					];

					microTasks.Add(context.MicroTasksFactory.CreateDeleteFileSystemItemsMicroTask(pathsToDelete, false, MicroTaskDescription.DeleteUnneededFiles.Instance));
				}

				microTasks.Add(context.MicroTasksFactory.CreateExtractSevenZipMicroTask(modpackFeedItem.DownloadInfo.FileName,
					SkyrimRelativePath.Root,
					MicroTaskDescription.ExtractModpack.Instance));

				foreach (var prerequisite in prerequisites)
				{
					microTasks.Add(context.MicroTasksFactory.CreateDownloadFileMicroTask(prerequisite.DownloadInfo));
					microTasks.Add(context.MicroTasksFactory.CreateRunProcessMicroTask(
						() => new ProcessStartInfo(_fileStorageService.GetFilePath(prerequisite.DownloadInfo.FileName)!, prerequisite.Arguments)
						{
							UseShellExecute = true,
						}, false, new MicroTaskDescription.InstallPrerequisite(prerequisite.Id)));
				}
				return microTasks;
			},
			[
				..selectComponentsTasks,
				await CreateInstallSkyrimTask(cancellationToken),
				.._prerequisitesAppTaskProvider.InstallVcRedistTasks,
				await _prerequisitesAppTaskProvider.CreateInstallSevenZipTask(cancellationToken),
			],
			installedVerifier);

		foreach (var patch in patches)
		{
			lastTask = await CreateInstallPatchTaskInternal(patch, true, [lastTask], cancellationToken);
		}

		foreach (var component in offered)
		{
			var id = component.Definition.Id;
			lastTask = new AppTask(
				$"{InstallModpackComponentTaskIdPrefix}{id}", new AppTaskDescription.InstallModpackComponent(id, component.Definition.Title),
				context => selection.InstallTasks.GetValueOrDefault(id)?.MicroTasksProvider(context) ?? [],
				[lastTask], installedVerifier)
			{
				IsUnneeded = () => selection.IsApplied && selection.InstallTasks.GetValueOrDefault(id) == null,
			};
		}
		return lastTask;
	}

	private async ValueTask<IReadOnlyList<ModComponentOption>> CreateModChoiceOptions(IReadOnlyList<ModChoiceOption> choices,
		IReadOnlyList<ModFeedData> mods, NuGetVersion modpackVersion, CancellationToken cancellationToken)
	{
		var options = new List<ModComponentOption>(choices.Count);
		foreach (var choice in choices)
		{
			if (mods.FirstOrDefault(x => x.Name == choice.ModName) is { } mod && mod.IsOfferedFor(modpackVersion))
			{
				options.Add(await CreateModChoiceOption(mod, choice.IsDefault, modpackVersion, cancellationToken));
			}
		}

		return options;
	}

	private async ValueTask<ModComponentOption> CreateModChoiceOption(ModFeedData feedData, bool isDefault,
		NuGetVersion modpackVersion, CancellationToken cancellationToken)
	{
		var name = feedData.DisplayName?.Resolve() ?? feedData.Name;
		var optionDescription = feedData.OptionDescription?.Resolve();
		if (feedData.FindLatestSupportedItem(modpackVersion) is not { } feedItem)
		{
			return new ModComponentOption(name, optionDescription, null, [], isDefault);
		}

		var installTask = await _installAppTaskProvider.CreateModInstallAppTask(feedData, feedItem.Version,
			cancellationToken);
		return new ModComponentOption(name, optionDescription, installTask, feedItem.GetDownloads().ToArray(), isDefault);
	}

	private async ValueTask<AppTask> CreateInstallSkyrimTask(CancellationToken cancellationToken)
	{
		var skyrimDownloadInfo = await _feedProvider.GetSkyrimDownloadInfo(cancellationToken);

		return new AppTask(
			"InstallSkyrim", AppTaskDescription.InstallSkyrim.Instance,
			context =>
			[
				context.MicroTasksFactory.CreateDownloadFileMicroTask(skyrimDownloadInfo),
				context.MicroTasksFactory.CreateExtractSevenZipMicroTask(skyrimDownloadInfo.FileName, SkyrimRelativePath.Root,
					MicroTaskDescription.InstallSkyrim.Instance),
			],
			[await _prerequisitesAppTaskProvider.CreateInstallSevenZipTask(cancellationToken)],
			new DelegateCompleteVerifier(_ =>
				ValueTask.FromResult(_skyrimInstallationChecker.CheckSkyrimDirectory(_appSettings.SkyrimDirectory)
					is SkyrimDirectoryCheckResult.SkyrimDirectory)));
	}

	/// <summary>
	/// Deletes the mods' backups after the Mod Organizer directory with the mods was deleted, so they are not restored
	/// into a later installation.
	/// </summary>
	private static IAppMicroTask CreateDeleteAllBackupsMicroTask(MicroTasksProviderContext context)
	{
		var installBackupService = context.ServiceProvider.GetRequiredService<IInstallBackupService>();
		return context.MicroTasksFactory.CreateDelegateMicroTask(
			(_, ct) => Task.Run(installBackupService.DeleteAll, ct).AsValueTask(), MicroTaskDescription.DeleteBackups.Instance);
	}
}