using System.ComponentModel;
using LauncherKit.Desktop.Extensions;
using LauncherKit.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Options;
using R3;
using LauncherKit.Core.Services.Abstractions;
using LauncherKit.Core.UserSettings;
using LauncherKit.Core.Utils;
using LauncherKit.Infrastructure;

namespace LauncherKit.Desktop.ViewModels;

public enum FirstSetupStep { Welcome, SkyrimDirectory, AppDataDirectory, Availability, Finished }
public enum FirstSetupState { NotStarted, InProgress, Saving, Finished }
public sealed record SetupError(Version DetectedSkyrimVersion, Version RequiredSkyrimVersion);

public sealed partial class FirstSetupViewModel : ViewModelBase
{
	private readonly ISettingsService _settingsService;
	private readonly ISkyrimInstallationChecker _installationChecker;
	private readonly IAppReloader _appReloader;
	private readonly AllSettings _settings;
	private readonly Version _requiredVersion;

	[ObservableProperty]
	public partial FirstSetupStep Step { get; private set; }
	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(IsFinished))]
	public partial FirstSetupState State { get; private set; }
	[ObservableProperty]
	public partial SetupError? Error { get; private set; }

	public bool IsFinished => State is FirstSetupState.Saving or FirstSetupState.Finished;
	public SkyrimDirectoryBrowserViewModel SkyrimDirectory { get; }
	public DirectoryBrowserViewModel AppDataDirectory { get; }
	public CheckAvailabilityViewModel Availability { get; }
	public AsyncRelayCommand ContinueCommand { get; }

	public FirstSetupViewModel(ISkyrimDirectoryFinder finder, ISettingsService settingsService,
		IFeedAvailabilityChecker availabilityChecker, ISkyrimInstallationChecker installationChecker,
		IAppReloader appReloader, IOptions<LauncherProductOptions> product, IUiDispatcher dispatcher)
	{
		_settingsService = settingsService;
		_installationChecker = installationChecker;
		_appReloader = appReloader;
		_settings = settingsService.GetSettings();
		_requiredVersion = Version.Parse(product.Value.RequiredSkyrimVersion);
		SkyrimDirectory = new SkyrimDirectoryBrowserViewModel(installationChecker)
		{
			ConflictingDirectoryError = DirectoryConflictErrors.SkyrimDirectory,
			SelectedDirectory = finder.FindSkyrimDirectory(),
			ConflictingDirectory = _settings.App.AppDataDirectory,
		};
		AppDataDirectory = new DirectoryBrowserViewModel
		{
			ConflictingDirectoryError = DirectoryConflictErrors.AppDataDirectory,
			SelectedDirectory = Path.GetDirectoryName(Directory.GetCurrentDirectory()),
			ConflictingDirectory = _settings.App.SkyrimDirectory,
		};
		Availability = new CheckAvailabilityViewModel(availabilityChecker, _ => Finish());
		ContinueCommand = new AsyncRelayCommand(Continue, CanContinue);
		foreach (var browser in new DirectoryBrowserViewModel[] { SkyrimDirectory, AppDataDirectory })
		{
			Observable.FromEventHandler<DataErrorsChangedEventArgs>(
				h => browser.ErrorsChanged += h, h => browser.ErrorsChanged -= h)
				.ObserveOnUi(dispatcher)
				.Subscribe(_ => ContinueCommand.NotifyCanExecuteChanged());
			browser.ObservePropertyChanged(x => x.SelectedDirectory)
				.ObserveOnUi(dispatcher)
				.Subscribe(_ => ContinueCommand.NotifyCanExecuteChanged());
		}
	}

	public Task Start()
	{
		if (State == FirstSetupState.NotStarted) State = FirstSetupState.InProgress;
		return Task.CompletedTask;
	}

	private bool CanContinue() => State == FirstSetupState.InProgress && Step switch
	{
		FirstSetupStep.Welcome => true,
		FirstSetupStep.SkyrimDirectory => !string.IsNullOrEmpty(SkyrimDirectory.SelectedDirectory) && !SkyrimDirectory.HasErrors,
		FirstSetupStep.AppDataDirectory => !string.IsNullOrEmpty(AppDataDirectory.SelectedDirectory) && !AppDataDirectory.HasErrors,
		_ => false,
	};

	private Task Continue()
	{
		Error = null;
		if (Step == FirstSetupStep.SkyrimDirectory)
		{
			if (_installationChecker.CheckSkyrimDirectory(SkyrimDirectory.SelectedDirectory!) is
				SkyrimDirectoryCheckResult.SkyrimDirectory(var version) && version != _requiredVersion)
			{
				Error = new SetupError(version, _requiredVersion);
				return Task.CompletedTask;
			}
			_settings.App.SkyrimDirectory = SkyrimDirectory.SelectedDirectory;
			AppDataDirectory.ConflictingDirectory = SkyrimDirectory.SelectedDirectory;
		}
		else if (Step == FirstSetupStep.AppDataDirectory)
			_settings.App.AppDataDirectory = AppDataDirectory.SelectedDirectory;

		Step = (int)Step < (int)FirstSetupStep.SkyrimDirectory && string.IsNullOrEmpty(_settings.App.SkyrimDirectory)
			? FirstSetupStep.SkyrimDirectory
			: (int)Step < (int)FirstSetupStep.AppDataDirectory && string.IsNullOrEmpty(_settings.App.AppDataDirectory)
				? FirstSetupStep.AppDataDirectory : FirstSetupStep.Availability;
		return Task.CompletedTask;
	}

	private async Task Finish()
	{
		State = FirstSetupState.Saving;
		await _settingsService.SaveSettings(_settings);
		State = FirstSetupState.Finished;
		Step = FirstSetupStep.Finished;
		_appReloader.ReloadApp(AppReloadMode.Configuration);
	}

	partial void OnStepChanged(FirstSetupStep value) => ContinueCommand.NotifyCanExecuteChanged();
	partial void OnStateChanged(FirstSetupState value) => ContinueCommand.NotifyCanExecuteChanged();
}
