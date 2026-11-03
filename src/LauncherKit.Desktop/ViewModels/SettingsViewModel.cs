using System.ComponentModel;
using LauncherKit.Desktop.Extensions;
using LauncherKit.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using R3;
using LauncherKit.Core.Extensions;
using LauncherKit.Core.Services.Abstractions;
using LauncherKit.Core.UserSettings;

namespace LauncherKit.Desktop.ViewModels;

public sealed partial class SettingsViewModel : BaseActivatableViewModel
{
	private const long BytesPerMegabyte = 1024 * 1024;

	private readonly ISettingsService _settingsService;

	public DirectoryBrowserViewModel SkyrimDirectoryBrowserViewModel { get; }

	public DirectoryBrowserViewModel AppDataDirectoryBrowserViewModel { get; }

	public DiscordIntegrationViewModel? DiscordIntegration { get; }

	public AsyncRelayCommand SaveSettingsCommand { get; }

	[ObservableProperty]
	public partial FileStoragePeriod FileStoragePeriod { get; set; }

	[ObservableProperty]
	public partial FileStoragePeriod PartialFileStoragePeriod { get; set; }

	[ObservableProperty]
	public partial double MaxDownloadSpeedMegabytesPerSecond { get; set; }

	[ObservableProperty]
	public partial string Language { get; set; } = string.Empty;

	public bool HaveUnsavedChanges { get; private set; }

	public SettingsViewModel(ISettingsService settingsService, IUiDispatcher dispatcher,
		DiscordIntegrationViewModel? discordIntegration = null)
	{
		_settingsService = settingsService;
		DiscordIntegration = discordIntegration;
		SkyrimDirectoryBrowserViewModel = new SkyrimDirectoryBrowserViewModel()
		{
			ConflictingDirectoryError = DirectoryConflictErrors.SkyrimDirectory,
		};
		AppDataDirectoryBrowserViewModel = new DirectoryBrowserViewModel()
		{
			ConflictingDirectoryError = DirectoryConflictErrors.AppDataDirectory,
		};
		SaveSettingsCommand = new AsyncRelayCommand(
			SaveSettings,
			() => !SkyrimDirectoryBrowserViewModel.HasErrors && !AppDataDirectoryBrowserViewModel.HasErrors);

		Observable
			.FromEventHandler<DataErrorsChangedEventArgs>(
				h => SkyrimDirectoryBrowserViewModel.ErrorsChanged += h,
				h => SkyrimDirectoryBrowserViewModel.ErrorsChanged -= h)
			.Merge(Observable
				.FromEventHandler<DataErrorsChangedEventArgs>(
					h => AppDataDirectoryBrowserViewModel.ErrorsChanged += h,
					h => AppDataDirectoryBrowserViewModel.ErrorsChanged -= h))
			.ObserveOnUi(dispatcher)
			.Subscribe(_ => SaveSettingsCommand.NotifyCanExecuteChanged());

		SkyrimDirectoryBrowserViewModel.ObservePropertyChanged(x => x.SelectedDirectory)
			.ObserveOnUi(dispatcher)
			.Subscribe(directory => AppDataDirectoryBrowserViewModel.ConflictingDirectory = directory);
		AppDataDirectoryBrowserViewModel.ObservePropertyChanged(x => x.SelectedDirectory)
			.ObserveOnUi(dispatcher)
			.Subscribe(directory => SkyrimDirectoryBrowserViewModel.ConflictingDirectory = directory);

		this.WhenActivated(d =>
		{
			var settings = settingsService.GetSettings();
			SkyrimDirectoryBrowserViewModel.SelectedDirectory = settings.App.SkyrimDirectory;
			AppDataDirectoryBrowserViewModel.SelectedDirectory = settings.App.AppDataDirectory;
			Language = settings.App.Language;
			FileStoragePeriod = settings.Download.FileStoragePeriod;
			PartialFileStoragePeriod = settings.Download.PartialFileStoragePeriod;
			MaxDownloadSpeedMegabytesPerSecond =
				settings.Download.MaxDownloadSpeedBytesPerSecond / (double)BytesPerMegabyte;
			HaveUnsavedChanges = false;

			this.ObserveAnyPropertyChanged()
				.Select(_ => Unit.Default)
				.Merge(SkyrimDirectoryBrowserViewModel.ObservePropertyChanged(x => x.SelectedDirectory, false).Select(_ => Unit.Default))
				.Merge(AppDataDirectoryBrowserViewModel.ObservePropertyChanged(x => x.SelectedDirectory, false).Select(_ => Unit.Default))
				.ObserveOnUi(dispatcher)
				.Subscribe(_ => HaveUnsavedChanges = true)
				.AddTo(d);
		});
	}

	private async Task SaveSettings()
	{
		var skyrimDirectory = SkyrimDirectoryBrowserViewModel.SelectedDirectory;
		var appDataDirectory = AppDataDirectoryBrowserViewModel.SelectedDirectory!;
		var fileStoragePeriod = FileStoragePeriod;
		var partialFileStoragePeriod = PartialFileStoragePeriod;
		var language = Language;
		var maxDownloadSpeedBytesPerSecond =
			(long)Math.Round(Math.Max(0, MaxDownloadSpeedMegabytesPerSecond) * BytesPerMegabyte);
		await _settingsService.ModifySettings(settings =>
		{
			settings.App.SkyrimDirectory = skyrimDirectory;
			settings.App.AppDataDirectory = appDataDirectory;
			settings.App.Language = language;
			settings.Download.FileStoragePeriod = fileStoragePeriod;
			settings.Download.PartialFileStoragePeriod = partialFileStoragePeriod;
			settings.Download.MaxDownloadSpeedBytesPerSecond = maxDownloadSpeedBytesPerSecond;
			return ValueTask.CompletedTask;
		});
		HaveUnsavedChanges = false;
	}
}