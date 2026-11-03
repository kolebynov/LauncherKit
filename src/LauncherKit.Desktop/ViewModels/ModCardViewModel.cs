using CommunityToolkit.Mvvm.ComponentModel;
using LauncherKit.Desktop.Extensions;
using CommunityToolkit.Mvvm.Input;
using R3;
using LauncherKit.Core.Models;

namespace LauncherKit.Desktop.ViewModels;

public sealed partial class ModCardViewModel : ViewModelBase
{
	public closed record InstallUnavailableReason
	{
		public sealed record Updating : InstallUnavailableReason;
		public sealed record OtherGraphPackInstalled(string Name) : InstallUnavailableReason;
		public sealed record ModpackTaskQueued : InstallUnavailableReason;
	}

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

	public bool InstallRequiresNewGame => _installRequiresNewGame && !IsInstalled;
	public bool UpdateRequiresNewGame { get; }
	public bool UninstallRequiresNewGame { get; }
	private readonly bool _installRequiresNewGame;

	public ModCardViewModel(string name, Uri logoUri, Uri descriptionUri, Func<ModInstallState> installStateFunc,
		AsyncRelayCommand installCommand, AsyncRelayCommand uninstallCommand,
		Func<InstallUnavailableReason>? unavailableReasonFunc = null,
		bool installRequiresNewGame = false, bool updateRequiresNewGame = false, bool uninstallRequiresNewGame = false)
	{
		Name = name;
		LogoUri = logoUri;
		DescriptionUri = descriptionUri;
		_installRequiresNewGame = installRequiresNewGame;
		UpdateRequiresNewGame = updateRequiresNewGame;
		UninstallRequiresNewGame = uninstallRequiresNewGame;
		SetInstallState(installStateFunc());
		InstallCommand = new AsyncRelayCommand(async () =>
		{
			await installCommand.ExecuteAsync(null);
			SetInstallState(installStateFunc());
		}, () => IsAvailable && (!IsInstalled || IsUpdateAvailable) && !uninstallCommand.IsRunning);
		UninstallCommand = new AsyncRelayCommand(async () =>
		{
			await uninstallCommand.ExecuteAsync(null);
			SetInstallState(installStateFunc());
		}, () => IsUninstallSupported && uninstallCommand.CanExecute(null) && !installCommand.IsRunning);
		installCommand.ObserveCanExecuteChange()
			.Subscribe(canExecute =>
			{
				UnavailableReason = canExecute ? null : unavailableReasonFunc?.Invoke() ?? new InstallUnavailableReason.Updating();
				IsAvailable = canExecute;
			});
		uninstallCommand.ObserveCanExecuteChange()
			.Subscribe(_ => UninstallCommand.NotifyCanExecuteChanged());
		this.ObservePropertyChanged(x => x.IsAvailable).Subscribe(_ => InstallCommand.NotifyCanExecuteChanged());
		// The install state changes after a command finishes, so the buttons must re-query both commands.
		this.ObservePropertyChanged(x => x.IsInstalled).Subscribe(_ => InstallCommand.NotifyCanExecuteChanged());
		this.ObservePropertyChanged(x => x.IsUpdateAvailable).Subscribe(_ => InstallCommand.NotifyCanExecuteChanged());
		this.ObservePropertyChanged(x => x.IsUninstallSupported).Subscribe(_ => UninstallCommand.NotifyCanExecuteChanged());
		installCommand.ObservePropertyChanged(x => x.IsRunning).Subscribe(_ => UninstallCommand.NotifyCanExecuteChanged());
		uninstallCommand.ObservePropertyChanged(x => x.IsRunning).Subscribe(_ => InstallCommand.NotifyCanExecuteChanged());
	}

	private void SetInstallState(ModInstallState state)
	{
		IsInstalled = state.IsInstalled;
		IsUpdateAvailable = state.IsUpdateAvailable;
		IsUninstallSupported = state.IsUninstallSupported;
		OnPropertyChanged(nameof(InstallRequiresNewGame));
	}
}