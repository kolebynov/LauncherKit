using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LauncherKit.Core.Services.Abstractions;
using LauncherKit.Core.Utils;
using LauncherKit.Desktop.Extensions;
using LauncherKit.Desktop.Services;
using LauncherKit.Infrastructure;
using Microsoft.Extensions.Options;
using R3;

namespace LauncherKit.Desktop.ViewModels.Setup;

/// <summary>
/// A step choosing a directory: <see cref="NextCommand"/> is available for a selected directory without validation
/// errors and moves on once <see cref="TryApply"/> accepts it.
/// </summary>
public abstract class DirectorySetupStepViewModel : SetupStepViewModel
{
	protected DirectorySetupStepViewModel(DirectoryBrowserViewModel directory, IUiDispatcher dispatcher)
	{
		Directory = directory;
		NextCommand = new AsyncRelayCommand(Next, CanGoNext);
		Observable.FromEventHandler<DataErrorsChangedEventArgs>(
				h => directory.ErrorsChanged += h, h => directory.ErrorsChanged -= h)
			.ObserveOnUi(dispatcher)
			.Subscribe(_ => NextCommand.NotifyCanExecuteChanged());
		directory.ObservePropertyChanged(x => x.SelectedDirectory)
			.ObserveOnUi(dispatcher)
			.Subscribe(_ => NextCommand.NotifyCanExecuteChanged());
	}

	public DirectoryBrowserViewModel Directory { get; }

	public AsyncRelayCommand NextCommand { get; }

	/// <summary>Validates the selected directory and applies it to the settings; <see langword="false"/> stays.</summary>
	protected abstract bool TryApply(string directory);

	protected override void OnShown() => NextCommand.NotifyCanExecuteChanged();

	protected override void OnHidden() => NextCommand.NotifyCanExecuteChanged();

	private bool CanGoNext() => IsShown && !string.IsNullOrEmpty(Directory.SelectedDirectory) && !Directory.HasErrors;

	private Task Next() => TryApply(Directory.SelectedDirectory!) ? GoNext() : Task.CompletedTask;
}

public sealed record SetupError(Version DetectedSkyrimVersion, Version RequiredSkyrimVersion);

/// <summary>
/// Chooses the Skyrim directory, an existing installation of the required version or a directory to install into.
/// </summary>
public partial class SkyrimDirectorySetupStepViewModel : DirectorySetupStepViewModel
{
	private readonly FirstSetupContext _context;
	private readonly ISkyrimInstallationChecker _installationChecker;
	private readonly Version _requiredVersion;

	/// <summary>Set when the chosen installation has another version; the step then stays.</summary>
	[ObservableProperty]
	public partial SetupError? Error { get; private set; }

	public SkyrimDirectorySetupStepViewModel(FirstSetupContext context, ISkyrimDirectoryFinder finder,
		ISkyrimInstallationChecker installationChecker, LauncherProductOptions product, IUiDispatcher dispatcher)
		: base(new SkyrimDirectoryBrowserViewModel(installationChecker)
		{
			ConflictingDirectoryError = DirectoryConflictErrors.SkyrimDirectory,
			SelectedDirectory = finder.FindSkyrimDirectory(),
			ConflictingDirectory = context.Settings.App.AppDataDirectory,
		}, dispatcher)
	{
		_context = context;
		_installationChecker = installationChecker;
		_requiredVersion = Version.Parse(product.RequiredSkyrimVersion);
	}

	protected override void OnShown()
	{
		Directory.ConflictingDirectory = _context.Settings.App.AppDataDirectory;
		base.OnShown();
	}

	protected override bool TryApply(string directory)
	{
		Error = null;
		if (_installationChecker.CheckSkyrimDirectory(directory) is SkyrimDirectoryCheckResult.SkyrimDirectory(var version)
		    && version != _requiredVersion)
		{
			Error = new SetupError(version, _requiredVersion);
			return false;
		}

		_context.Settings.App.SkyrimDirectory = directory;
		return true;
	}
}

/// <summary>Chooses the directory of downloads and temporary files; defaults to the launchers root.</summary>
public class AppDataDirectorySetupStepViewModel : DirectorySetupStepViewModel
{
	private readonly FirstSetupContext _context;

	public AppDataDirectorySetupStepViewModel(FirstSetupContext context, IUiDispatcher dispatcher)
		: base(new DirectoryBrowserViewModel
		{
			ConflictingDirectoryError = DirectoryConflictErrors.AppDataDirectory,
			SelectedDirectory = Path.GetDirectoryName(System.IO.Directory.GetCurrentDirectory()),
			ConflictingDirectory = context.Settings.App.SkyrimDirectory,
		}, dispatcher)
	{
		_context = context;
	}

	protected override void OnShown()
	{
		Directory.ConflictingDirectory = _context.Settings.App.SkyrimDirectory;
		base.OnShown();
	}

	protected override bool TryApply(string directory)
	{
		_context.Settings.App.AppDataDirectory = directory;
		return true;
	}
}

/// <summary>
/// Whether the first setup still needs the download server check: until a check succeeded, stored under
/// <see cref="CheckSucceededKey"/> in the global key-value storage, so installations configured before the key existed
/// get the check too. Continuing without downloads skips the check for the rest of the run (the setup's configuration
/// reload would otherwise require it again at once); the next start offers it again. A singleton, as scopes are
/// recreated on reload.
/// </summary>
public sealed class AvailabilityCheckState(IGlobalKeyValueStorage storage)
{
	public const string CheckSucceededKey = "FeedAvailabilityCheckSucceeded";

	private bool _skippedThisRun;

	public bool IsRequired => !_skippedThisRun && !storage.HasKey(CheckSucceededKey);

	public void Succeeded() => storage.Set(CheckSucceededKey, "1");

	public void ContinuedWithoutDownloads() => _skippedThisRun = true;
}

/// <summary>
/// Checks the download servers; moves on once the check finished, even when no server works. A success is recorded in
/// <see cref="AvailabilityCheckState"/> at once, continuing after a failure only for this run.
/// </summary>
public class AvailabilitySetupStepViewModel : SetupStepViewModel
{
	public AvailabilitySetupStepViewModel(IFeedAvailabilityChecker availabilityChecker, AvailabilityCheckState state)
	{
		Availability = new CheckAvailabilityViewModel(availabilityChecker, _ =>
		{
			if (!Availability!.IsAvailable) state.ContinuedWithoutDownloads();
			return GoNext();
		});
		Availability.PropertyChanged += (_, e) =>
		{
			if (e.PropertyName == nameof(CheckAvailabilityViewModel.IsAvailable) && Availability.IsAvailable)
				state.Succeeded();
		};
	}

	public CheckAvailabilityViewModel Availability { get; }
}

/// <summary>Offers the Skyrim directory step until a Skyrim directory is set.</summary>
public sealed class SkyrimDirectorySetupStepProvider(ISkyrimDirectoryFinder finder,
	ISkyrimInstallationChecker installationChecker, IOptions<LauncherProductOptions> product, IUiDispatcher dispatcher)
	: ISetupStepProvider
{
	public int Order => SetupStepOrder.SkyrimDirectory;

	public IEnumerable<SetupStepViewModel> CreateSteps(FirstSetupContext context) =>
		string.IsNullOrEmpty(context.Settings.App.SkyrimDirectory)
			? [new SkyrimDirectorySetupStepViewModel(context, finder, installationChecker, product.Value, dispatcher)]
			: [];
}

/// <summary>Offers the data directory step until a data directory is set.</summary>
public sealed class AppDataDirectorySetupStepProvider(IUiDispatcher dispatcher) : ISetupStepProvider
{
	public int Order => SetupStepOrder.AppDataDirectory;

	public IEnumerable<SetupStepViewModel> CreateSteps(FirstSetupContext context) =>
		string.IsNullOrEmpty(context.Settings.App.AppDataDirectory)
			? [new AppDataDirectorySetupStepViewModel(context, dispatcher)]
			: [];
}

/// <summary>Offers the download server check while <see cref="AvailabilityCheckState.IsRequired"/>.</summary>
public sealed class AvailabilitySetupStepProvider(IFeedAvailabilityChecker availabilityChecker, AvailabilityCheckState state)
	: ISetupStepProvider
{
	public int Order => SetupStepOrder.Availability;

	public IEnumerable<SetupStepViewModel> CreateSteps(FirstSetupContext context) =>
		state.IsRequired ? [new AvailabilitySetupStepViewModel(availabilityChecker, state)] : [];
}
