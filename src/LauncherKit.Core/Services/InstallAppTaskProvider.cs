using Microsoft.Extensions.Options;
using LauncherKit.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NuGet.Versioning;
using LauncherKit.Core.AppTasks;
using LauncherKit.Core.AppTasks.InstallSteps;
using LauncherKit.Core.AppTasks.MicroTasks;
using LauncherKit.Core.Backups;
using LauncherKit.Core.Exceptions;
using LauncherKit.Core.Extensions;
using LauncherKit.Core.Feed.Mods;
using LauncherKit.Core.FileStorage;
using LauncherKit.Core.Models;
using LauncherKit.Core.Services.Abstractions;

namespace LauncherKit.Core.Services;

public sealed class InstallAppTaskProvider : IInstallAppTaskProvider
{
	private readonly IPrerequisitesAppTaskProvider _prerequisitesAppTaskProvider;
	private readonly IFileStorageService _fileStorageService;
	private readonly IModOrganizerService _modOrganizerService;
	private readonly IModpackVersionProvider _modpackVersionProvider;
	private readonly IInstallBackupService _installBackupService;
	private readonly ILogger<InstallAppTaskProvider> _logger;
	private readonly LauncherProductOptions _product;

	public InstallAppTaskProvider(IPrerequisitesAppTaskProvider prerequisitesAppTaskProvider,
		IFileStorageService fileStorageService, IModOrganizerService modOrganizerService,
		IModpackVersionProvider modpackVersionProvider, IInstallBackupService installBackupService,
		ILogger<InstallAppTaskProvider> logger, IOptions<LauncherProductOptions> product)
	{
		_prerequisitesAppTaskProvider = prerequisitesAppTaskProvider;
		_fileStorageService = fileStorageService;
		_modOrganizerService = modOrganizerService;
		_modpackVersionProvider = modpackVersionProvider;
		_installBackupService = installBackupService;
		_logger = logger;
		_product = product.Value;
	}

	public async ValueTask<AppTask> CreateModInstallAppTask(ModFeedData modFeed, NuGetVersion? version,
		CancellationToken cancellationToken)
	{
		var modName = modFeed.Name;
		return new AppTask($"Install_{modName}", new AppTaskDescription.InstallMod(modName),
			context =>
			{
				var microTasksFactory = context.MicroTasksFactory;
				var installStepsMicroTasksFactory = context.ServiceProvider.GetRequiredService<IInstallStepsMicroTasksFactory>();
				var installBackupService = context.ServiceProvider.GetRequiredService<IInstallBackupService>();
				ModFeedItem modFeedItem;
				if (version != null)
				{
					// The caller vouches for the version, so it is installed even when the cached installed modpack version
					// is stale, e.g. right after the modpack itself was installed by a preceding task.
					modFeedItem = modFeed.Items.FirstOrDefault(x => x.Version == version) ??
					              throw new ArgumentException($"Mod {modName} version {version} was not found", nameof(version));
				}
				else
				{
					var modpackVersion = _modpackVersionProvider.InstalledModpackVersion ?? throw new ModpackNotInstalledException();
					modFeedItem = modFeed.Items.Where(x => x.SupportedModpackVersion.Satisfies(modpackVersion)).MaxBy(x => x.Version) ??
					              throw new ModNotSupportedModpackVersionException(modName, modpackVersion);
				}

				var displayName = modFeed.DisplayName?.Resolve() ?? modFeed.Name;
				var archiveName = modFeedItem.ArchiveDownloadInfo.FileName;
				var microTasks = new List<IAppMicroTask>();
				IEnumerable<ModPatchFeedItem> patches;
				var isInstalled = _modOrganizerService.IsModInstalled(modName);
				var unfinishedVersion = isInstalled ? GetUnfinishedVersion(modFeed) : null;
				var installedVersion = isInstalled ? unfinishedVersion ?? ReadModVersion(modFeed) : null;
				if (unfinishedVersion == null && installedVersion != null && modFeedItem.Includes(installedVersion) &&
				    installedVersion < modFeedItem.GetLatestVersion())
				{
					// The installed mod is this version with some of its patches missing: only those are applied.
					patches = modFeedItem.GetPatchesAfter(installedVersion);
				}
				else
				{
					if (isInstalled)
					{
						if (installedVersion == null || modFeed.FindItem(installedVersion) is not { } installedFeedItem)
						{
							throw new InvalidOperationException(
								$"Installed version {installedVersion} of mod {modName} was not found in the feed");
						}

						microTasks.AddRange(CreateRollbackMicroTasks(modFeed, installedFeedItem, installedVersion,
							MicroTaskDescription.DeletePreviousVersion.Instance, microTasksFactory, installStepsMicroTasksFactory,
							installBackupService));
					}
					else
					{
						// Left by a mod removed without rolling it back (e.g. its directory deleted by the user): restoring
						// them later would bring back files that may be outdated since.
						microTasks.Add(CreateDeleteBackupsMicroTask(modName, microTasksFactory, installBackupService));
					}

					var archiveDownloadInfo = modFeedItem.ArchiveDownloadInfo;
					var backupId = InstallBackupId.ForModVersion(modName, modFeedItem.Version);
					microTasks.Add(microTasksFactory.CreateDownloadFileMicroTask(archiveDownloadInfo));
					// Until meta.ini gets the version, the mod directory does not tell which version it holds, so the
					// mark tells a retry of an interrupted installation which version's steps to roll back.
					microTasks.Add(microTasksFactory.CreateDelegateMicroTask(
						(_, ct) => installBackupService.StartInstallation(backupId, ct).AsValueTask(),
						MicroTaskDescription.PreparingInstallation.Instance));
					microTasks.Add(microTasksFactory.CreateInstallModMicroTask(modName, archiveDownloadInfo.FileName,
						modFeedItem.Placement));
					microTasks.AddRange(installStepsMicroTasksFactory.CreateInstallMicroTasks(modFeedItem.InstallSteps ?? [],
						backupId));
					microTasks.Add(CreateUpdateMetaIniMicroTask(modName, archiveName, modFeedItem.Version, displayName,
						microTasksFactory));
					microTasks.Add(microTasksFactory.CreateDelegateMicroTask(
						(_, ct) => Task.Run(() => installBackupService.FinishInstallation(backupId), ct).AsValueTask(),
						MicroTaskDescription.FinishInstallation.Instance));
					patches = modFeedItem.GetPatchesAfter(modFeedItem.Version);
				}

				var modDirectory = new SkyrimRelativePath(_product.ModOrganizerDirectoryName).Append("mods").Append(modName);
				foreach (var patch in patches)
				{
					var patchArchiveName = patch.DownloadInfo.FileName;
					microTasks.Add(microTasksFactory.CreateDownloadFileMicroTask(patch.DownloadInfo));
					microTasks.Add(microTasksFactory.CreateExtractSevenZipMicroTask(patchArchiveName, modDirectory,
						new MicroTaskDescription.ExtractModPatch(patch.Version)));
					microTasks.AddRange(installStepsMicroTasksFactory.CreateInstallMicroTasks(patch.InstallSteps ?? [],
						InstallBackupId.ForModVersion(modName, patch.Version)));
					// Written after each patch's steps, so an interrupted update leaves the version of the last applied
					// patch and a patch whose steps failed or were interrupted is still offered after a restart.
					microTasks.Add(CreateUpdateMetaIniMicroTask(modName, archiveName, patch.Version, displayName,
						microTasksFactory));
				}

				return microTasks;
			}, [.._prerequisitesAppTaskProvider.InstallVcRedistTasks, await _prerequisitesAppTaskProvider.CreateInstallSevenZipTask(cancellationToken)], null);
	}

	public AppTask CreateModUninstallAppTask(ModFeedData modFeed)
	{
		var modName = modFeed.Name;
		return new AppTask($"Uninstall_{modName}", new AppTaskDescription.UninstallMod(modName),
			context =>
			{
				var microTasksFactory = context.MicroTasksFactory;
				if (!_modOrganizerService.IsModInstalled(modName))
				{
					_logger.LogInformation("Mod {ModName} is not installed, nothing to uninstall", modName);
					return [];
				}

				var installedVersion = GetInstalledVersion(modFeed);
				if (modFeed.FindItem(installedVersion) is { } installedFeedItem)
				{
					return installedFeedItem.SupportsUninstall
						? CreateRollbackMicroTasks(modFeed, installedFeedItem, installedVersion, new MicroTaskDescription.UninstallMod(modName),
							microTasksFactory, context.ServiceProvider.GetRequiredService<IInstallStepsMicroTasksFactory>(),
							context.ServiceProvider.GetRequiredService<IInstallBackupService>())
						: throw new InvalidOperationException(
							$"Installed version {installedVersion} of mod {modName} does not support uninstallation");
				}

				// Without the feed item there are no steps to roll back, so only the mod itself can be removed.
				_logger.LogWarning("Installed version {Version} of mod {ModName} is not in the feed, only removing the mod",
					installedVersion, modName);
				return
				[
					microTasksFactory.CreateDelegateMicroTask(
						(_, ct) => _modOrganizerService.UninstallMod(modName, ct).AsValueTask(), new MicroTaskDescription.UninstallMod(modName)),
				];
			}, [], null);
	}

	public ModInstallState GetModInstallState(ModFeedData modFeed, NuGetVersion? modpackVersion)
	{
		if (!_modOrganizerService.IsModInstalled(modFeed.Name))
		{
			return ModInstallState.NotInstalled;
		}

		var installedVersion = GetInstalledVersion(modFeed);
		if (modFeed.FindItem(installedVersion) is not { } installedFeedItem)
		{
			// Not updatable without its feed item, but the mod directory can still be removed.
			return ModInstallState.Installed(isUpdateAvailable: false, isUninstallSupported: true);
		}

		// A newer version or a patch of the installed one that is not applied yet.
		var isUpdateAvailable = modpackVersion != null &&
		                        modFeed.Items.Any(x => x.GetLatestVersion() > installedVersion &&
		                                               x.SupportedModpackVersion.Satisfies(modpackVersion));
		return ModInstallState.Installed(isUpdateAvailable, installedFeedItem.SupportsUninstall);
	}

	/// <summary>
	/// Writes <paramref name="version"/> to the mod's meta.ini, with the stored path of the mod archive
	/// <paramref name="archiveName"/> resolved when the microtask runs, after its download.
	/// </summary>
	private IAppMicroTask CreateUpdateMetaIniMicroTask(string modName, string archiveName, NuGetVersion version,
		string displayName, IMicroTasksFactory microTasksFactory) =>
		microTasksFactory.CreateDelegateMicroTask(
			(_, ct) => _modOrganizerService
				.CreateOrUpdateMetaIniFile(modName, _fileStorageService.GetFilePath(archiveName), version, displayName, ct)
				.AsValueTask(),
			MicroTaskDescription.UpdateModMetadata.Instance);

	/// <summary>
	/// The version of the installed mod. A version whose installation was interrupted is the installed one, whatever
	/// its meta.ini says.
	/// </summary>
	private NuGetVersion GetInstalledVersion(ModFeedData modFeed) => GetUnfinishedVersion(modFeed) ?? ReadModVersion(modFeed);

	/// <summary>
	/// The version whose installation started and did not finish (the installation failed or was interrupted before
	/// meta.ini got the version), or <see langword="null"/> when there is none.
	/// </summary>
	private NuGetVersion? GetUnfinishedVersion(ModFeedData modFeed) =>
		modFeed.Items
			.Select(item => item.Version)
			.FirstOrDefault(version =>
				_installBackupService.IsInstallationUnfinished(InstallBackupId.ForModVersion(modFeed.Name, version)));

	/// <summary>The version recorded in the mod's meta.ini, 1.0.0 when it records none.</summary>
	private NuGetVersion ReadModVersion(ModFeedData modFeed)
	{
		var modInfo = _modOrganizerService.InstalledMods.FirstOrDefault(x => x.Name == modFeed.Name);
		return NuGetVersion.TryParse(modInfo?.Version, out var version) ? version : new NuGetVersion(1, 0, 0);
	}

	/// <summary>
	/// Rolls back the install steps of the patches applied up to <paramref name="installedVersion"/>, newest first, then
	/// the installed version's own install steps, then uninstalls the mod and deletes what is left of its backups.
	/// </summary>
	private List<IAppMicroTask> CreateRollbackMicroTasks(ModFeedData modFeed, ModFeedItem installedFeedItem,
		NuGetVersion installedVersion, MicroTaskDescription uninstallDescription, IMicroTasksFactory microTasksFactory,
		IInstallStepsMicroTasksFactory installStepsMicroTasksFactory, IInstallBackupService installBackupService)
	{
		var modName = modFeed.Name;
		var microTasks = new List<IAppMicroTask>(
			installedFeedItem.GetAppliedPatches(installedVersion)
				.Reverse()
				.SelectMany(patch => installStepsMicroTasksFactory.CreateRollbackMicroTasks(patch.InstallSteps ?? [],
					InstallBackupId.ForModVersion(modName, patch.Version)))
				.Concat(installStepsMicroTasksFactory.CreateRollbackMicroTasks(installedFeedItem.InstallSteps ?? [],
					InstallBackupId.ForModVersion(modName, installedFeedItem.Version))))
		{
			microTasksFactory.CreateDelegateMicroTask(
				(_, ct) => _modOrganizerService.UninstallMod(modName, ct).AsValueTask(), uninstallDescription),
			CreateDeleteBackupsMicroTask(modName, microTasksFactory, installBackupService),
		};

		return microTasks;
	}

	/// <summary>Deletes every backup of the mod <paramref name="modName"/> without restoring it.</summary>
	private static IAppMicroTask CreateDeleteBackupsMicroTask(string modName, IMicroTasksFactory microTasksFactory,
		IInstallBackupService installBackupService) =>
		microTasksFactory.CreateDelegateMicroTask(
			(_, ct) => Task.Run(() => installBackupService.Delete(InstallBackupId.ForMod(modName)), ct).AsValueTask(),
			MicroTaskDescription.DeleteBackups.Instance);
}