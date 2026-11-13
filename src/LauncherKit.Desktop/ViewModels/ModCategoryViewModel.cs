using CommunityToolkit.Mvvm.ComponentModel;
using LauncherKit.Desktop.Extensions;
using LauncherKit.Desktop.Services;
using CommunityToolkit.Mvvm.Input;
using LauncherKit.Infrastructure;
using R3;
using LauncherKit.Core.AppTasks;
using LauncherKit.Core.Feed;
using LauncherKit.Core.Feed.Mods;
using LauncherKit.Core.Services.Abstractions;
using LauncherKit.Core.Utils;

namespace LauncherKit.Desktop.ViewModels;

/// <summary>
/// A section of mod cards for one feed category, loaded while activated. A mod without a version supporting the installed
/// modpack is shown unavailable, unless it is discontinued, in which case it is hidden.
/// </summary>
public partial class ModCategoryViewModel : BaseActivatableViewModel
{
	private readonly ModCategoryFeedData _category;
	private readonly ModCategoriesViewModel _owner;
	private readonly IInstallAppTaskProvider _installAppTaskProvider;
	private readonly IAppTaskQueue _appTaskQueue;
	private readonly IFeedProvider _feedProvider;
	private readonly IModpackVersionProvider _modpackVersionProvider;
	private readonly IModOrganizerService _modOrganizerService;
	private readonly IFeedUtils _feedUtils;

	/// <summary>
	/// Names of every mod of an exclusive category, including the hidden ones, which may still be installed; empty for a
	/// category that is not exclusive.
	/// </summary>
	private string[] _exclusiveModNames = [];

	/// <summary>The installed mod of an exclusive category; only one can be installed at a time.</summary>
	private string? _installedModName;

	/// <summary>
	/// The mod of an exclusive category whose install command is running until its task finishes; kept by the owner so
	/// that it survives the recreation of this view model.
	/// </summary>
	private string? InstallingModName => _owner.InstallingExclusiveMods.GetValueOrDefault(_category.Id);

	/// <summary>
	/// Install commands of the cards, disabled while the modpack is being installed and, in an exclusive category, except
	/// for its own card, while another mod is installed or being installed.
	/// </summary>
	private AsyncRelayCommand[] _installCommands = [];

	/// <summary>Uninstall commands of the cards, disabled while the modpack is being installed.</summary>
	private AsyncRelayCommand[] _uninstallCommands = [];

	public string Id => _category.Id;

	public string Title { get; }

	/// <summary>The category's Markdown description, shown in the section header, if any.</summary>
	public string? Description { get; }

	/// <summary>The feed server root, against which the description's relative links and images resolve.</summary>
	public Uri DescriptionAssetRoot { get; }

	[ObservableProperty]
	public partial IReadOnlyList<ModCardViewModel> Mods { get; private set; } = [];

	public Loader Loader { get; } = new();

	public ModCategoryViewModel(ModCategoryFeedData category, ModCategoriesViewModel owner,
		IInstallAppTaskProvider installAppTaskProvider, IAppTaskQueue appTaskQueue, IFeedProvider feedProvider,
		IModpackVersionProvider modpackVersionProvider, IModOrganizerService modOrganizerService, IFeedUtils feedUtils,
		IUiDispatcher dispatcher)
	{
		_category = category;
		_owner = owner;
		_installAppTaskProvider = installAppTaskProvider;
		_appTaskQueue = appTaskQueue;
		_feedProvider = feedProvider;
		_modpackVersionProvider = modpackVersionProvider;
		_modOrganizerService = modOrganizerService;
		_feedUtils = feedUtils;
		Title = category.Title.Resolve();
		Description = category.Description?.Resolve();
		DescriptionAssetRoot = feedUtils.GetUri(string.Empty);

		this.WhenActivated(d =>
		{
			_modpackVersionProvider.ObservePropertyChanged(x => x.InstalledModpackVersion)
				.ObserveOnUi(dispatcher)
				.SubscribeAwait((_, ct) => UpdateMods(ct))
				.AddTo(d);

			// The modpack installation may install a mod, so the installed one is re-read once it ends.
			_owner.ObservePropertyChanged(x => x.IsModpackInstalling)
				.ObserveOnUi(dispatcher)
				.Subscribe(_ =>
				{
					RefreshInstalledMod();
					foreach (var uninstallCommand in _uninstallCommands)
					{
						uninstallCommand.NotifyCanExecuteChanged();
					}
				})
				.AddTo(d);
		});
	}

	private async ValueTask UpdateMods(CancellationToken cancellationToken)
	{
		using var loading = Loader.BeginLoading();
		var modpackVersion = _modpackVersionProvider.InstalledModpackVersion;
		var categoryMods = (await _feedProvider.GetModFeedDatas(cancellationToken))
			.Where(x => x.CategoryId == _category.Id)
			.ToArray();
		_exclusiveModNames = _category.IsExclusive ? categoryMods.Select(x => x.Name).ToArray() : [];
		_installedModName = FindInstalledMod();
		var mods = categoryMods.Where(x => x.IsOfferedFor(modpackVersion)).ToArray();
		_installCommands = new AsyncRelayCommand[mods.Length];
		_uninstallCommands = new AsyncRelayCommand[mods.Length];
		var cards = new ModCardViewModel[mods.Length];
		for (var i = 0; i < mods.Length; i++)
		{
			var mod = mods[i];
			var availableItem = modpackVersion != null ? mod.FindLatestSupportedItem(modpackVersion) : null;
			_installCommands[i] = availableItem != null
				? new AsyncRelayCommand(ct => InstallMod(mod, availableItem, ct),
					() => !_owner.IsModpackInstalling && GetOtherModReason(mod.Name) == null)
				: new AsyncRelayCommand(() => Task.CompletedTask, () => false);
			_uninstallCommands[i] = new AsyncRelayCommand(ct => UninstallMod(mod, ct), () => !_owner.IsModpackInstalling);
			cards[i] = new ModCardViewModel(
				mod.DisplayName?.Resolve() ?? mod.Name, _feedUtils.GetUri(mod.LogoPath),
				_feedUtils.GetUri(mod.DescriptionPath.Resolve()),
				() => _installAppTaskProvider.GetModInstallState(mod, modpackVersion), _installCommands[i],
				_uninstallCommands[i],
				() => _owner.IsModpackInstalling
					? ModCardViewModel.InstallUnavailableReason.ModpackTaskQueued.Instance
					: availableItem == null
						? ModCardViewModel.InstallUnavailableReason.NotUpdated.Instance
						: GetOtherModReason(mod.Name) ?? ModCardViewModel.InstallUnavailableReason.NotUpdated.Instance,
				installRequiresNewGame: mod.RequiresNewGame, uninstallRequiresNewGame: mod.RequiresNewGame);
		}

		Mods = cards;
	}

	/// <summary>
	/// Why <paramref name="modName"/> cannot be installed because of another mod of its exclusive category, or
	/// <see langword="null"/> when no other mod of it is installed or being installed.
	/// </summary>
	private ModCardViewModel.InstallUnavailableReason? GetOtherModReason(string modName)
	{
		if (_installedModName != null && _installedModName != modName)
		{
			return new ModCardViewModel.InstallUnavailableReason.OtherModInstalled(_installedModName);
		}

		return InstallingModName is { } installingModName && installingModName != modName
			? new ModCardViewModel.InstallUnavailableReason.OtherModInstalled(installingModName)
			: null;
	}

	private string? FindInstalledMod() => _exclusiveModNames.FirstOrDefault(_modOrganizerService.IsModInstalled);

	private void RefreshInstalledMod()
	{
		_installedModName = FindInstalledMod();
		NotifyInstallCommands();
	}

	private void NotifyInstallCommands()
	{
		foreach (var installCommand in _installCommands)
		{
			installCommand.NotifyCanExecuteChanged();
		}
	}

	private async Task InstallMod(ModFeedData mod, ModFeedItem feedItem, CancellationToken cancellationToken)
	{
		if (_category.IsExclusive)
		{
			// A card recreated during a pending install of its own mod would only re-enqueue a duplicate, which the queue
			// drops at once; it must neither start it nor release the pending install's lock.
			if (InstallingModName != null)
				return;
			_owner.InstallingExclusiveMods[_category.Id] = mod.Name;
			NotifyInstallCommands();
		}

		try
		{
			var appTask = await _installAppTaskProvider.CreateModInstallAppTask(mod, feedItem.Version, cancellationToken);
			await _appTaskQueue.EnqueueAndWaitForCompletion(appTask, cancellationToken);
		}
		finally
		{
			if (_category.IsExclusive)
				_owner.InstallingExclusiveMods.Remove(_category.Id);
			RefreshInstalledMod();
			// The category may have been recreated meanwhile; its cards still show the state read before this install.
			foreach (var category in _owner.Categories)
			{
				if (category != this && category.Id == _category.Id)
					category.RefreshInstallStates();
			}
		}
	}

	private void RefreshInstallStates()
	{
		RefreshInstalledMod();
		foreach (var card in Mods)
		{
			card.RefreshInstallState();
		}
	}

	private async Task UninstallMod(ModFeedData mod, CancellationToken cancellationToken)
	{
		try
		{
			await _appTaskQueue.EnqueueAndWaitForCompletion(_installAppTaskProvider.CreateModUninstallAppTask(mod),
				cancellationToken);
		}
		finally
		{
			RefreshInstalledMod();
		}
	}
}
