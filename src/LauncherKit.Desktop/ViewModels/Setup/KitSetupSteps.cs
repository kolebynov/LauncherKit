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
public sealed partial class SkyrimDirectorySetupStepViewModel : DirectorySetupStepViewModel
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
public sealed class AppDataDirectorySetupStepViewModel : DirectorySetupStepViewModel
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

/// <summary>Checks the download servers; moves on once the check finished, even when no server works.</summary>
public sealed class AvailabilitySetupStepViewModel : SetupStepViewModel
{
	public AvailabilitySetupStepViewModel(IFeedAvailabilityChecker availabilityChecker) =>
		Availability = new CheckAvailabilityViewModel(availabilityChecker, _ => GoNext());

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

/// <summary>
/// Offers the download server check while the installation is being configured, i.e. while the Skyrim or the data
/// directory is not set. It alone never makes the setup required.
/// </summary>
public sealed class AvailabilitySetupStepProvider(IFeedAvailabilityChecker availabilityChecker) : ISetupStepProvider
{
	public int Order => SetupStepOrder.Availability;

	public IEnumerable<SetupStepViewModel> CreateSteps(FirstSetupContext context) =>
		string.IsNullOrEmpty(context.Settings.App.SkyrimDirectory) || string.IsNullOrEmpty(context.Settings.App.AppDataDirectory)
			? [new AvailabilitySetupStepViewModel(availabilityChecker)]
			: [];
}
