using CommunityToolkit.Mvvm.ComponentModel;
using LauncherKit.Desktop.Extensions;
using LauncherKit.Desktop.Services;
using CommunityToolkit.Mvvm.Input;
using R3;
using LauncherKit.Core.Feed.Mods;
using LauncherKit.Core.Services.Abstractions;
using LauncherKit.Core.Utils;
using LauncherKit.Infrastructure;

namespace LauncherKit.Desktop.ViewModels;

/// <summary>
/// A mod's card: its installed facts and install and uninstall commands, read from the queued installations of
/// <see cref="IInstallationService"/> on each activation and kept current while activated. Installing is unavailable while
/// the modpack installation chain or removal is queued, without a version supporting the installed modpack, and, in an
/// exclusive category, while another mod of it, hidden ones included, is installed or being installed. Uninstalling is
/// unavailable while the modpack is queued, and neither runs while the mod itself is queued.
/// </summary>
public sealed partial class ModCardViewModel : BaseActivatableViewModel
{
	public closed record InstallUnavailableReason
	{
		/// <summary>The mod has no version supporting the installed modpack version yet.</summary>
		public sealed record NotUpdated : InstallUnavailableReason;
		/// <summary>Another mod of the same exclusive category is installed or being installed.</summary>
		public sealed record OtherModInstalled(string Name) : InstallUnavailableReason;
		public sealed record ModpackTaskQueued : InstallUnavailableReason;
	}

	private readonly ModFeedData _mod;
	private readonly ModCategoryFeedData _category;
	private readonly IReadOnlyList<ModFeedData> _categoryMods;
	private readonly IInstallationService _installations;
	private readonly IModpackVersionProvider _modpackVersionProvider;
	private ModFeedItem? _availableItem;
	private bool _isModpackQueued;
	private bool _isModQueued;

	public string Name { get; }

	[ObservableProperty]
	public partial bool IsAvailable { get; private set; }

	/// <summary>Why the install command cannot run, shown as the card tooltip; <see langword="null"/> when it can.</summary>
	[ObservableProperty]
	public partial InstallUnavailableReason? UnavailableReason { get; private set; }

	public Uri LogoUri { get; }

	[ObservableProperty]
	public partial bool IsInstalled { get; private set; }

	[ObservableProperty]
	public partial bool IsUpdateAvailable { get; private set; }

	/// <summary>The installed version can be uninstalled, so the card offers <see cref="UninstallCommand"/>.</summary>
	[ObservableProperty]
	public partial bool IsUninstallSupported { get; private set; }

	/// <summary>Installs the mod when it is not installed, or updates it when an update is available.</summary>
	public AsyncRelayCommand InstallCommand { get; }

	/// <summary>Uninstalls the installed mod; never runs together with <see cref="InstallCommand"/>.</summary>
	public AsyncRelayCommand UninstallCommand { get; }

	public Uri DescriptionUri { get; }

	public bool InstallRequiresNewGame => _mod.RequiresNewGame && !IsInstalled;
	public bool UninstallRequiresNewGame => _mod.RequiresNewGame;

	/// <param name="mod">The card's mod.</param>
	/// <param name="category">The mod's category.</param>
	/// <param name="categoryMods">Every mod of <paramref name="category"/>, including hidden ones, which may still be installed.</param>
	public ModCardViewModel(ModFeedData mod, ModCategoryFeedData category, IReadOnlyList<ModFeedData> categoryMods,
		IInstallationService installations, IModpackVersionProvider modpackVersionProvider, IFeedUtils feedUtils,
		IUiDispatcher dispatcher)
	{
		_mod = mod;
		_category = category;
		_categoryMods = categoryMods;
		_installations = installations;
		_modpackVersionProvider = modpackVersionProvider;
		Name = mod.DisplayName?.Resolve() ?? mod.Name;
		LogoUri = feedUtils.GetUri(mod.LogoPath);
		DescriptionUri = feedUtils.GetUri(mod.DescriptionPath.Resolve());
		InstallCommand = new AsyncRelayCommand(Install,
			() => IsAvailable && (!IsInstalled || IsUpdateAvailable) && !_isModQueued && !UninstallCommand!.IsRunning);
		UninstallCommand = new AsyncRelayCommand(Uninstall,
			() => IsUninstallSupported && !_isModpackQueued && !_isModQueued && !InstallCommand.IsRunning);
		InstallCommand.ObservePropertyChanged(x => x.IsRunning).Subscribe(_ => UninstallCommand.NotifyCanExecuteChanged());
		UninstallCommand.ObservePropertyChanged(x => x.IsRunning).Subscribe(_ => InstallCommand.NotifyCanExecuteChanged());
		// Subscribed before the snapshot, so no change is missed, and both run on the UI thread, so the card's state is only
		// read and written there. Changes posted before the activation ended are dropped: the next activation re-reads them.
		this.WhenActivated(d =>
		{
			var isActive = true;
			Disposable.Create(() => isActive = false).AddTo(d);
			installations.Changes.ObserveOnUi(dispatcher).Subscribe(_ =>
			{
				if (isActive)
					Refresh();
			}).AddTo(d);
			Refresh();
		});
	}

	private async Task Install(CancellationToken cancellationToken)
	{
		if (_availableItem is not { } item)
			return;
		await _installations.InstallMod(_mod, _category, item.Version, cancellationToken);
		Refresh();
	}

	private async Task Uninstall(CancellationToken cancellationToken)
	{
		try
		{
			await _installations.UninstallMod(_mod, cancellationToken);
		}
		finally
		{
			Refresh();
		}
	}

	private void Refresh()
	{
		var state = _installations.GetModInstallState(_mod);
		IsInstalled = state.IsInstalled;
		IsUpdateAvailable = state.IsUpdateAvailable;
		IsUninstallSupported = state.IsUninstallSupported;
		OnPropertyChanged(nameof(InstallRequiresNewGame));

		var modpackVersion = _modpackVersionProvider.InstalledModpackVersion;
		_availableItem = modpackVersion != null ? _mod.FindLatestSupportedItem(modpackVersion) : null;
		var queued = _installations.QueuedInstallations;
		_isModpackQueued = queued.Any(x => x is QueuedInstallation.Modpack);
		_isModQueued = queued.Any(x => x is QueuedInstallation.ModInstall(var m) && m.Name == _mod.Name ||
			x is QueuedInstallation.ModUninstall(var u) && u.Name == _mod.Name);
		UnavailableReason = _isModpackQueued
			? InstallUnavailableReason.ModpackTaskQueued.Instance
			: _availableItem == null
				? InstallUnavailableReason.NotUpdated.Instance
				: GetOtherModReason(queued);
		IsAvailable = UnavailableReason == null;
		InstallCommand.NotifyCanExecuteChanged();
		UninstallCommand.NotifyCanExecuteChanged();
	}

	/// <summary>
	/// Why the mod cannot be installed because of another mod of its exclusive category, or <see langword="null"/> when no
	/// other mod of it is installed or being installed.
	/// </summary>
	private InstallUnavailableReason? GetOtherModReason(IReadOnlyList<QueuedInstallation> queued)
	{
		if (!_category.IsExclusive)
			return null;

		var installed = _categoryMods.FirstOrDefault(x => _installations.GetModInstallState(x).IsInstalled);
		if (installed != null && installed.Name != _mod.Name)
			return new InstallUnavailableReason.OtherModInstalled(installed.Name);

		return queued.Select(x => x is QueuedInstallation.ModInstall(var m) ? m : null)
			.FirstOrDefault(x => x != null && x.CategoryId == _category.Id && x.Name != _mod.Name) is { } installing
			? new InstallUnavailableReason.OtherModInstalled(installing.Name)
			: null;
	}
}
