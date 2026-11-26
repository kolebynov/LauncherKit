using Microsoft.Extensions.Options;
using LauncherKit.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NuGet.Versioning;
using R3;
using LauncherKit.Core.AppTasks;
using LauncherKit.Core.AppTasks.InstallSteps;
using LauncherKit.Core.AppTasks.MicroTasks;
using LauncherKit.Core.Backups;
using LauncherKit.Core.Choices;
using LauncherKit.Core.Exceptions;
using LauncherKit.Core.Extensions;
using LauncherKit.Core.Feed;
using LauncherKit.Core.Feed.Install;
using LauncherKit.Core.Feed.Mods;
using LauncherKit.Core.FileStorage;
using LauncherKit.Core.Models;
using LauncherKit.Core.ModDependencies;
using LauncherKit.Core.Services.Abstractions;

namespace LauncherKit.Core.Services;

public sealed class InstallAppTaskProvider : IInstallAppTaskProvider
{
	private const string ResolveModDependenciesTaskIdPrefix = "ResolveModDependencies_";

	private readonly IPrerequisitesAppTaskProvider _prerequisitesAppTaskProvider;
	private readonly IFileStorageService _fileStorageService;
	private readonly IModOrganizerService _modOrganizerService;
	private readonly IModpackVersionProvider _modpackVersionProvider;
	private readonly IFeedProvider _feedProvider;
	private readonly IInstallBackupService _installBackupService;
	private readonly ILogger<InstallAppTaskProvider> _logger;
	private readonly LauncherProductOptions _product;

	public InstallAppTaskProvider(IPrerequisitesAppTaskProvider prerequisitesAppTaskProvider,
		IFileStorageService fileStorageService, IModOrganizerService modOrganizerService,
		IModpackVersionProvider modpackVersionProvider, IFeedProvider feedProvider, IInstallBackupService installBackupService,
		ILogger<InstallAppTaskProvider> logger, IOptions<LauncherProductOptions> product)
	{
		_prerequisitesAppTaskProvider = prerequisitesAppTaskProvider;
		_fileStorageService = fileStorageService;
		_modOrganizerService = modOrganizerService;
		_modpackVersionProvider = modpackVersionProvider;
		_feedProvider = feedProvider;
		_installBackupService = installBackupService;
		_logger = logger;
		_product = product.Value;
	}

	public async ValueTask<AppTask> CreateModInstallAppTask(ModFeedData modFeed, NuGetVersion? version,
		CancellationToken cancellationToken)
	{
		var modName = modFeed.Name;
		var keptMods = new KeptMods();
		return new AppTask($"Install_{modName}", new AppTaskDescription.InstallMod(modName),
			context =>
			{
				var modFeedItem = FindItemToInstall(modFeed, version);
				return CreateInstallMicroTasks(context, modFeed, modFeedItem, modFeedItem.GetLatestVersion(),
					skipWhenInstalled: false, [modFeedItem], keptMods);
			}, await CreatePrerequisiteTasks(cancellationToken), null);
	}

	public async ValueTask<AppTask> CreateModInstallWithDependenciesAppTask(ModFeedData modFeed, NuGetVersion? version,
		ModDependencyReservations reservations, CancellationToken cancellationToken)
	{
		if (!modFeed.Items.Any(x => x.Dependencies is { Count: > 0 }))
		{
			return await CreateModInstallAppTask(modFeed, version, cancellationToken);
		}

		var modName = modFeed.Name;
		var plan = new DependencyPlan();
		var keptMods = new KeptMods();
		// Both tasks of a request are its own: a retry must not be taken for the resolution of a cancelled request still
		// leaving the queue, nor a request made while the previous installation of the mod finishes for that installation.
		var requestId = Guid.NewGuid().ToString("N");
		var resolveTask = new AppTask($"{ResolveModDependenciesTaskIdPrefix}{modName}_{requestId}",
			new AppTaskDescription.ResolveModDependencies(modName),
			context =>
			[
				context.MicroTasksFactory.CreateDelegateMicroTask(
					(steps, ct) => ResolveDependencies(modFeed, version, reservations, plan, steps, ct),
					MicroTaskDescription.ResolveModDependencies.Instance),
			],
			await CreatePrerequisiteTasks(cancellationToken), null);
		return new AppTask($"Install_{modName}_{requestId}", new AppTaskDescription.InstallMod(modName),
			context =>
			{
				var (item, installs) = plan.Result ??
				                       throw new InvalidOperationException($"Dependencies of mod {modName} are not resolved");
				return CreatePlanMicroTasks(context, modFeed, item, installs, keptMods);
			}, [resolveTask], null)
		{
			// Declining the confirmation, or the request ending during the resolution, installs nothing.
			IsUnneeded = () => plan.IsDeclined,
		};
	}

	/// <summary>
	/// The planned installations, then <paramref name="item"/> of <paramref name="modFeed"/>. Each mod's microtasks are
	/// created only once the installations before it ran, from the state they left: one of them may have installed the
	/// mod meanwhile (e.g. through an <c>installMod</c> step), which then is kept when it has the planned version,
	/// updated when it is older and fails the installation when it is newer.
	/// </summary>
	private IEnumerable<IAppMicroTask> CreatePlanMicroTasks(MicroTasksProviderContext context, ModFeedData modFeed,
		ModFeedItem item, IReadOnlyList<PlannedModInstall> installs, KeptMods keptMods)
	{
		// Rolling back an installed version keeps the mods these releases declare as dependencies.
		IReadOnlyCollection<ModFeedItem> plannedItems = [..installs.Select(x => x.Item), item];
		foreach (var install in installs)
		{
			if (_modOrganizerService.IsModInstalled(install.Mod.Name) && GetUnfinishedVersion(install.Mod) == null &&
			    _modOrganizerService.ReadModVersion(install.Mod.Name) is var installedVersion &&
			    installedVersion > install.TargetVersion)
			{
				throw new ModDependencyUnavailableException(modFeed,
					[new ModDependencyOption(install.Mod, null, null,
						new ModDependencyUnavailableReason.NewerVersionInstalled(installedVersion))]);
			}

			foreach (var microTask in CreateInstallMicroTasks(context, install.Mod, install.Item, install.TargetVersion,
				         skipWhenInstalled: true, plannedItems, keptMods))
			{
				yield return microTask;
			}
		}

		foreach (var microTask in CreateInstallMicroTasks(context, modFeed, item, item.GetLatestVersion(),
			         skipWhenInstalled: false, plannedItems, keptMods))
		{
			yield return microTask;
		}
	}

	private async ValueTask<IReadOnlyList<AppTask>> CreatePrerequisiteTasks(CancellationToken cancellationToken) =>
		[.._prerequisitesAppTaskProvider.InstallVcRedistTasks, await _prerequisitesAppTaskProvider.CreateInstallSevenZipTask(cancellationToken)];

	/// <summary>
	/// The feed version <paramref name="version"/>, or the newest version supporting the installed modpack when
	/// <see langword="null"/>.
	/// </summary>
	private ModFeedItem FindItemToInstall(ModFeedData modFeed, NuGetVersion? version)
	{
		if (version != null)
		{
			// The caller vouches for the version, so it is installed even when the cached installed modpack version
			// is stale, e.g. right after the modpack itself was installed by a preceding task.
			return modFeed.Items.FirstOrDefault(x => x.Version == version) ??
			       throw new ArgumentException($"Mod {modFeed.Name} version {version} was not found", nameof(version));
		}

		var modpackVersion = _modpackVersionProvider.InstalledModpackVersion ?? throw new ModpackNotInstalledException();
		return modFeed.FindLatestSupportedItem(modpackVersion) ??
		       throw new ModNotSupportedModpackVersionException(modFeed.Name, modpackVersion);
	}

	/// <summary>
	/// Plans the installations the version to install needs first, asking which alternative to install and whether
	/// installed dependencies may be updated and first-time dependencies requiring a new game installed; declining, or the
	/// installation request ending meanwhile, leaves <paramref name="plan"/> declined. The plan is reserved atomically
	/// with the requester's other tracked installations.
	/// </summary>
	private async ValueTask ResolveDependencies(ModFeedData modFeed, NuGetVersion? version,
		ModDependencyReservations reservations, DependencyPlan plan, ISubject<MicroTaskStep> steps,
		CancellationToken cancellationToken)
	{
		using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, reservations.RequestEnded);
		try
		{
			await ResolveDependencies(modFeed, version, reservations, plan, steps, lifetime.Token, cancellationToken);
		}
		catch (OperationCanceledException) when (reservations.RequestEnded.IsCancellationRequested &&
		                                         !cancellationToken.IsCancellationRequested)
		{
			_logger.LogInformation("Installation of mod {ModName} ended while resolving its dependencies", modFeed.Name);
			plan.IsDeclined = true;
		}
	}

	private async ValueTask ResolveDependencies(ModFeedData modFeed, NuGetVersion? version,
		ModDependencyReservations reservations, DependencyPlan plan, ISubject<MicroTaskStep> steps,
		CancellationToken cancellationToken, CancellationToken taskCancellationToken)
	{
		var item = FindItemToInstall(modFeed, version);
		var modpackVersion = _modpackVersionProvider.InstalledModpackVersion ?? throw new ModpackNotInstalledException();
		var mods = await _feedProvider.GetModFeedDatas(cancellationToken);
		var categories = await _feedProvider.GetModCategories(cancellationToken);
		var installedMods = new Dictionary<string, InstalledModVersion>(StringComparer.Ordinal);
		foreach (var mod in mods.Where(x => _modOrganizerService.IsModInstalled(x.Name)))
		{
			var unfinishedVersion = GetUnfinishedVersion(mod);
			installedMods.Add(mod.Name, new InstalledModVersion(
				unfinishedVersion ?? _modOrganizerService.ReadModVersion(mod.Name), unfinishedVersion == null));
		}

		var resolver = new ModDependencyResolver(mods, categories, modpackVersion, installedMods,
			reservations.GetQueuedInstalls(), (choice, ct) => ChooseDependency(choice, steps, ct));
		var installs = await resolver.Resolve(modFeed, item, item.GetLatestVersion(), cancellationToken);
		var upgrades = installs
			.Where(x => x.InstalledVersion != null)
			.Select(x => new ModUpgrade(x.Mod, x.InstalledVersion!, x.TargetVersion))
			.ToArray();
		var newGameInstalls = installs
			.Where(x => x.InstalledVersion == null && x.Mod.RequiresNewGame)
			.Select(x => x.Mod)
			.ToArray();
		if (upgrades.Length > 0 || newGameInstalls.Length > 0)
		{
			using var confirmation = new ModUpgradesConfirmation(modFeed, upgrades, newGameInstalls, cancellationToken);
			steps.OnNext(new MicroTaskStep.ConfirmModUpgrades(confirmation));
			if (!await confirmation.ResultTask)
			{
				_logger.LogInformation("Installing dependencies of mod {ModName} was declined", modFeed.Name);
				plan.IsDeclined = true;
				return;
			}
		}

		foreach (var install in installs)
		{
			_logger.LogInformation("Mod {ModName} needs {Dependency} {Version} (installed: {InstalledVersion})",
				modFeed.Name, install.Mod.Name, install.TargetVersion, install.InstalledVersion);
		}

		cancellationToken.ThrowIfCancellationRequested();
		if (reservations.TryReserve(installs.Select(x => x.Mod).ToArray(), categories) is { } conflict)
		{
			throw new ModDependencyUnavailableException(modFeed,
				[new ModDependencyOption(conflict.Mod, null, null,
					new ModDependencyUnavailableReason.OtherModInExclusiveCategory(conflict.QueuedMod))]);
		}

		plan.Result = (item, installs);
	}

	private static async ValueTask<ModDependencyOption> ChooseDependency(ModDependencyChoice choice,
		ISubject<MicroTaskStep> steps, CancellationToken cancellationToken)
	{
		var firstAvailable = choice.Options.First(x => x.IsAvailable);
		var options = choice.Options
			.Select(x => new ChoiceOption
			{
				Name = new ChoiceText.Content(x.Mod.ResolveDisplayName()),
				Description = (x.Install, x.KeptVersion) switch
				{
					({ } install, _) => new ChoiceText.ModDependencyOptionDescription(install.TargetVersion, install.InstalledVersion),
					(null, { } keptVersion) => new ChoiceText.ModDependencyKeptDescription(keptVersion),
					_ => new ChoiceText.Content(string.Empty),
				},
				UnavailableReason = x.UnavailableReason is { } reason ? (ChoiceText?)new ChoiceText.UnavailableModDependency(reason) : null,
				IsSelectedByDefault = x == firstAvailable,
			})
			.ToArray();
		var dependentName = choice.Dependent.ResolveDisplayName();
		ChoiceStep step = new()
		{
			SelectionMode = ChoiceSelectionMode.ExactlyOne,
			Options = options,
		};
		using var context = new ChoiceSelectionContext(
			new ChoiceText.SelectModDependencyTitle(dependentName), [step], cancellationToken);
		steps.OnNext(new MicroTaskStep.SelectChoices(context));
		var selection = await context.ResultTask;
		return choice.Options[Array.IndexOf(options, selection.Single().SelectedOptions.Single())];
	}

	/// <summary>
	/// Installs <paramref name="modFeedItem"/> with its patches up to <paramref name="targetVersion"/>: an installed
	/// version is rolled back first, unless it is this version with some of those patches missing, when only they are
	/// applied. With <paramref name="skipWhenInstalled"/>, a finished installation of <paramref name="targetVersion"/>
	/// is kept as is. The rollback keeps the mods <paramref name="plannedItems"/>, the releases the task installs,
	/// declare as dependencies, and the new version's <c>installMod</c> actions reuse the mods it kept, also in a later
	/// attempt of the task (<paramref name="keptModsOfTask"/>), after the rollback already ran.
	/// </summary>
	private List<IAppMicroTask> CreateInstallMicroTasks(MicroTasksProviderContext context, ModFeedData modFeed,
		ModFeedItem modFeedItem, NuGetVersion targetVersion, bool skipWhenInstalled,
		IReadOnlyCollection<ModFeedItem> plannedItems, KeptMods keptModsOfTask)
	{
		var modName = modFeed.Name;
		var microTasksFactory = context.MicroTasksFactory;
		var installStepsMicroTasksFactory = context.ServiceProvider.GetRequiredService<IInstallStepsMicroTasksFactory>();
		var installBackupService = context.ServiceProvider.GetRequiredService<IInstallBackupService>();
		var displayName = modFeed.ResolveDisplayName();
		var archiveName = modFeedItem.ArchiveDownloadInfo.FileName;
		var microTasks = new List<IAppMicroTask>();
		// Filled by the rollback when it runs, read by the new version's installMod actions; kept for the task's later
		// attempts, which no longer roll back. Mod directories are matched like Windows paths.
		var keptMods = keptModsOfTask.Of(modName);
		IEnumerable<ModPatchFeedItem> patches;
		var isInstalled = _modOrganizerService.IsModInstalled(modName);
		var unfinishedVersion = isInstalled ? GetUnfinishedVersion(modFeed) : null;
		var installedVersion = isInstalled ? unfinishedVersion ?? _modOrganizerService.ReadModVersion(modName) : null;
		if (skipWhenInstalled && unfinishedVersion == null && installedVersion == targetVersion)
		{
			_logger.LogInformation("Mod {ModName} {Version} is already installed", modName, targetVersion);
			return microTasks;
		}

		if (unfinishedVersion == null && installedVersion != null && modFeedItem.Includes(installedVersion) &&
		    installedVersion < targetVersion)
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
					installBackupService, plannedItems, keptMods));
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
				backupId, keptMods));
			microTasks.Add(CreateUpdateMetaIniMicroTask(modName, archiveName, modFeedItem.Version, displayName,
				microTasksFactory));
			microTasks.Add(microTasksFactory.CreateDelegateMicroTask(
				(_, ct) => Task.Run(() => installBackupService.FinishInstallation(backupId), ct).AsValueTask(),
				MicroTaskDescription.FinishInstallation.Instance));
			patches = modFeedItem.GetPatchesAfter(modFeedItem.Version);
		}

		var modDirectory = new SkyrimRelativePath(_product.ModOrganizerDirectoryName).Append("mods").Append(modName);
		foreach (var patch in patches.Where(x => x.Version <= targetVersion))
		{
			var patchArchiveName = patch.DownloadInfo.FileName;
			microTasks.Add(microTasksFactory.CreateDownloadFileMicroTask(patch.DownloadInfo));
			microTasks.Add(microTasksFactory.CreateExtractSevenZipMicroTask(patchArchiveName, modDirectory,
				new MicroTaskDescription.ExtractModPatch(patch.Version)));
			microTasks.AddRange(installStepsMicroTasksFactory.CreateInstallMicroTasks(patch.InstallSteps ?? [],
				InstallBackupId.ForModVersion(modName, patch.Version), keptMods));
			// Written after each patch's steps, so an interrupted update leaves the version of the last applied
			// patch and a patch whose steps failed or were interrupted is still offered after a restart.
			microTasks.Add(CreateUpdateMetaIniMicroTask(modName, archiveName, patch.Version, displayName,
				microTasksFactory));
		}

		return microTasks;
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
							context.ServiceProvider.GetRequiredService<IInstallBackupService>(), plannedItems: [], keptMods: null)
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
	private NuGetVersion GetInstalledVersion(ModFeedData modFeed) =>
		GetUnfinishedVersion(modFeed) ?? _modOrganizerService.ReadModVersion(modFeed.Name);

	/// <summary>
	/// The version whose installation started and did not finish (the installation failed or was interrupted before
	/// meta.ini got the version), or <see langword="null"/> when there is none.
	/// </summary>
	private NuGetVersion? GetUnfinishedVersion(ModFeedData modFeed) =>
		modFeed.Items
			.Select(item => item.Version)
			.FirstOrDefault(version =>
				_installBackupService.IsInstallationUnfinished(InstallBackupId.ForModVersion(modFeed.Name, version)));

	/// <summary>
	/// Rolls back the install steps of the patches applied up to <paramref name="installedVersion"/>, newest first, then
	/// the installed version's own install steps, then uninstalls the mod and deletes what is left of its backups.
	/// The rollback of an <see cref="InstallAction.InstallMod"/> action keeps its mod installed when a dependency of
	/// <paramref name="plannedItems"/> (the releases the task installs) or of the installed release of another installed
	/// mod names it in any alternative (names compared case-insensitively, like the mod directories); whether another
	/// mod's release names it is read when the action is rolled back. Each kept mod is recorded in
	/// <paramref name="keptMods"/>, when given, with the version its meta.ini records.
	/// </summary>
	private List<IAppMicroTask> CreateRollbackMicroTasks(ModFeedData modFeed, ModFeedItem installedFeedItem,
		NuGetVersion installedVersion, MicroTaskDescription uninstallDescription, IMicroTasksFactory microTasksFactory,
		IInstallStepsMicroTasksFactory installStepsMicroTasksFactory, IInstallBackupService installBackupService,
		IReadOnlyCollection<ModFeedItem> plannedItems, IDictionary<string, NuGetVersion>? keptMods)
	{
		var modName = modFeed.Name;
		var keepsMod = CreateKeepsDependency(modName, plannedItems, keptMods);
		var microTasks = new List<IAppMicroTask>(
			installedFeedItem.GetAppliedPatches(installedVersion)
				.Reverse()
				.SelectMany(patch => installStepsMicroTasksFactory.CreateRollbackMicroTasks(patch.InstallSteps ?? [],
					InstallBackupId.ForModVersion(modName, patch.Version), keepsMod))
				.Concat(installStepsMicroTasksFactory.CreateRollbackMicroTasks(installedFeedItem.InstallSteps ?? [],
					InstallBackupId.ForModVersion(modName, installedFeedItem.Version), keepsMod)))
		{
			microTasksFactory.CreateDelegateMicroTask(
				(_, ct) => _modOrganizerService.UninstallMod(modName, ct).AsValueTask(), uninstallDescription),
			CreateDeleteBackupsMicroTask(modName, microTasksFactory, installBackupService),
		};

		return microTasks;
	}

	/// <summary>
	/// Whether rolling back <paramref name="rolledBackModName"/> keeps a mod one of its actions installed: a dependency
	/// of <paramref name="plannedItems"/>, or of the installed release of another installed mod, names it. A kept mod is
	/// recorded in <paramref name="keptMods"/>, when given, with the version its meta.ini records.
	/// </summary>
	private Func<string, CancellationToken, ValueTask<bool>> CreateKeepsDependency(string rolledBackModName,
		IReadOnlyCollection<ModFeedItem> plannedItems, IDictionary<string, NuGetVersion>? keptMods) =>
		async (modName, cancellationToken) =>
		{
			if (plannedItems.Any(x => DeclaresDependency(x, modName)))
			{
				_logger.LogInformation("Mod {ModName} is a dependency of a release being installed", modName);
				keptMods?[modName] = _modOrganizerService.ReadModVersion(modName);
				return true;
			}

			var mods = await _feedProvider.GetModFeedDatas(cancellationToken);
			var dependent = mods.FirstOrDefault(x => !IsSameMod(x.Name, rolledBackModName) && !IsSameMod(x.Name, modName) &&
			                                         _modOrganizerService.IsModInstalled(x.Name) &&
			                                         x.FindItem(GetInstalledVersion(x)) is { } item &&
			                                         DeclaresDependency(item, modName));
			if (dependent == null)
			{
				return false;
			}

			_logger.LogInformation("Mod {ModName} is a dependency of the installed mod {Dependent}", modName, dependent.Name);
			keptMods?[modName] = _modOrganizerService.ReadModVersion(modName);
			return true;
		};

	/// <summary>
	/// Whether two mod names name the same mod directory: a legacy <c>installMod</c> step may spell a feed mod's name
	/// in another case, which Windows treats as the same directory.
	/// </summary>
	private static bool IsSameMod(string name, string otherName) =>
		string.Equals(name, otherName, StringComparison.OrdinalIgnoreCase);

	private static bool DeclaresDependency(ModFeedItem item, string modName) =>
		(item.Dependencies ?? []).Any(dependency => dependency switch
		{
			ModDependency.AnotherMod(var alternatives) => alternatives.Any(x => IsSameMod(x.ModName, modName)),
		});

	/// <summary>Deletes every backup of the mod <paramref name="modName"/> without restoring it.</summary>
	private static IAppMicroTask CreateDeleteBackupsMicroTask(string modName, IMicroTasksFactory microTasksFactory,
		IInstallBackupService installBackupService) =>
		microTasksFactory.CreateDelegateMicroTask(
			(_, ct) => Task.Run(() => installBackupService.Delete(InstallBackupId.ForMod(modName)), ct).AsValueTask(),
			MicroTaskDescription.DeleteBackups.Instance);

	/// <summary>What the dependency resolution of a mod installation decided, read by the installation task.</summary>
	private sealed class DependencyPlan
	{
		/// <summary>The version to install and the installations that precede it, once resolved.</summary>
		public (ModFeedItem Item, IReadOnlyList<PlannedModInstall> Installs)? Result { get; set; }

		public bool IsDeclined { get; set; }
	}

	/// <summary>
	/// The mods the rollbacks of one task kept installed, with the versions they kept, per mod being replaced, shared by
	/// every attempt of the task: a resumed attempt finds the replaced mod already rolled back and still reuses what its
	/// rollback kept.
	/// </summary>
	private sealed class KeptMods
	{
		private readonly Dictionary<string, Dictionary<string, NuGetVersion>> _byReplacedMod =
			new(StringComparer.OrdinalIgnoreCase);

		public Dictionary<string, NuGetVersion> Of(string replacedModName)
		{
			if (!_byReplacedMod.TryGetValue(replacedModName, out var keptMods))
			{
				keptMods = new Dictionary<string, NuGetVersion>(StringComparer.OrdinalIgnoreCase);
				_byReplacedMod.Add(replacedModName, keptMods);
			}

			return keptMods;
		}
	}
}