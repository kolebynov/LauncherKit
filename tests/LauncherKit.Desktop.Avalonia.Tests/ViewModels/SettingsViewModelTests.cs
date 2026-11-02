using Microsoft.Extensions.Logging;
using LauncherKit.Desktop.Avalonia.Extensions;
using LauncherKit.Desktop.Extensions;
using LauncherKit.Desktop.Services;
using LauncherKit.Desktop.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;
using LauncherKit.Core.Services.Abstractions;
using LauncherKit.Core.UserSettings;
using LauncherKit.Infrastructure;
using LauncherKit.TestSupport;

namespace LauncherKit.Desktop.Avalonia.Tests.ViewModels;

[TestFixture]
public sealed class SettingsViewModelTests
{
	[TestCase("")]
	[TestCase("en")]
	[TestCase("ru")]
	public async Task SaveSettings_ValidChangedValues_PersistsAndClearsDirtyState(string language)
	{
		// Arrange
		using var skyrim = new TempDirectory();
		using var appData = new TempDirectory();
		var settings = new AllSettings
		{
			App = new AppSettings { SkyrimDirectory = skyrim.Path, AppDataDirectory = appData.Path },
			Download = new DownloadSettings { FileStoragePeriod = FileStoragePeriod.Week },
			Window = new WindowSettings(),
		};
		var mocker = CreateMocker();
		mocker.GetMock<ISettingsService>().Setup(x => x.GetSettings()).Returns(settings);
		mocker.GetMock<ISettingsService>()
			.Setup(x => x.ModifySettings(It.IsAny<Func<AllSettings, ValueTask>>()))
			.Returns(async (Func<AllSettings, ValueTask> modify) => await modify(settings));
		var sut = CreateSettings(mocker);
		using var activation = await sut.Activator.Activate(CancellationToken.None);
		sut.FileStoragePeriod = FileStoragePeriod.Month;
		sut.MaxDownloadSpeedMegabytesPerSecond = 1.5;
		sut.Language = language;

		// Act
		await sut.SaveSettingsCommand.ExecuteAsync(null);

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(settings.Download.FileStoragePeriod, Is.EqualTo(FileStoragePeriod.Month));
			Assert.That(settings.Download.MaxDownloadSpeedBytesPerSecond, Is.EqualTo(1536 * 1024));
			Assert.That(settings.App.Language, Is.EqualTo(language));
			Assert.That(sut.HaveUnsavedChanges, Is.False);
		});
		mocker.GetMock<ISettingsService>()
			.Verify(x => x.ModifySettings(It.IsAny<Func<AllSettings, ValueTask>>()), Times.Once);
	}

	[Test]
	public async Task Activate_StoredSpeedLimitInBytes_IsShownInMegabytesPerSecond()
	{
		// Arrange
		using var skyrim = new TempDirectory();
		using var appData = new TempDirectory();
		var settings = new AllSettings
		{
			App = new AppSettings { SkyrimDirectory = skyrim.Path, AppDataDirectory = appData.Path },
			Download = new DownloadSettings { MaxDownloadSpeedBytesPerSecond = 3 * 1024 * 1024 },
			Window = new WindowSettings(),
		};
		var mocker = CreateMocker();
		mocker.GetMock<ISettingsService>().Setup(x => x.GetSettings()).Returns(settings);
		var sut = CreateSettings(mocker);

		// Act
		using var activation = await sut.Activator.Activate(CancellationToken.None);

		// Assert
		Assert.That(sut.MaxDownloadSpeedMegabytesPerSecond, Is.EqualTo(3d));
	}

	[TestCase("")]
	[TestCase("en")]
	[TestCase("ru")]
	public async Task Activate_StoredLanguage_RestoresSelection(string language)
	{
		var settings = new AllSettings
		{
			App = new AppSettings { Language = language },
			Download = new DownloadSettings(),
			Window = new WindowSettings(),
		};
		var mocker = CreateMocker();
		mocker.GetMock<ISettingsService>().Setup(x => x.GetSettings()).Returns(settings);
		var sut = CreateSettings(mocker);

		using var activation = await sut.Activator.Activate(CancellationToken.None);

		Assert.That(sut.Language, Is.EqualTo(language));
		Assert.That(sut.HaveUnsavedChanges, Is.False);
	}

	[Test]
	public async Task SaveSettingsCommand_AppDataDirectoryNestedIntoSkyrimDirectory_CannotExecuteAndReportsFieldMessage()
	{
		// Arrange
		using var skyrim = new TempDirectory();
		using var appData = new TempDirectory();
		var settings = new AllSettings
		{
			App = new AppSettings { SkyrimDirectory = skyrim.Path, AppDataDirectory = appData.Path },
			Download = new DownloadSettings(),
			Window = new WindowSettings(),
		};
		var mocker = CreateMocker();
		mocker.GetMock<ISettingsService>().Setup(x => x.GetSettings()).Returns(settings);
		var sut = CreateSettings(mocker);
		using var activation = await sut.Activator.Activate(CancellationToken.None);

		// Act
		sut.AppDataDirectoryBrowserViewModel.SelectedDirectory = skyrim.Combine("LauncherData");

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(
				sut.AppDataDirectoryBrowserViewModel
					.GetErrors(nameof(DirectoryBrowserViewModel.SelectedDirectory)),
				Is.EqualTo(new[] { DirectoryConflictErrors.AppDataDirectory }));
			Assert.That(
				sut.SkyrimDirectoryBrowserViewModel
					.GetErrors(nameof(DirectoryBrowserViewModel.SelectedDirectory)),
				Is.EqualTo(new[] { DirectoryConflictErrors.SkyrimDirectory }));
			Assert.That(sut.SaveSettingsCommand.CanExecute(null), Is.False);
		});
	}

	[Test]
	public async Task SkyrimDirectoryChanged_MovedOutOfAppDataDirectory_ClearsConflictError()
	{
		// Arrange
		using var appData = new TempDirectory();
		using var other = new TempDirectory();
		var settings = new AllSettings
		{
			App = new AppSettings { SkyrimDirectory = appData.Combine("Skyrim"), AppDataDirectory = appData.Path },
			Download = new DownloadSettings(),
			Window = new WindowSettings(),
		};
		var mocker = CreateMocker();
		mocker.GetMock<ISettingsService>().Setup(x => x.GetSettings()).Returns(settings);
		var sut = CreateSettings(mocker);
		using var activation = await sut.Activator.Activate(CancellationToken.None);
		Assert.That(sut.SaveSettingsCommand.CanExecute(null), Is.False);

		// Act
		sut.SkyrimDirectoryBrowserViewModel.SelectedDirectory = other.Combine("Skyrim");

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(sut.SkyrimDirectoryBrowserViewModel.HasErrors, Is.False);
			Assert.That(sut.AppDataDirectoryBrowserViewModel.HasErrors, Is.False);
			Assert.That(sut.SaveSettingsCommand.CanExecute(null), Is.True);
		});
	}

	[Test]
	public void Constructor_DiscordNotConfigured_AllowsMissingIntegration()
	{
		var service = new Mock<ISettingsService>(MockBehavior.Strict);
		var dispatcher = new Mock<IUiDispatcher>(MockBehavior.Strict);
		dispatcher.Setup(x => x.Post(It.IsAny<Action>())).Callback<Action>(action => action());
		var sut = new SettingsViewModel(service.Object, dispatcher.Object);
		Assert.That(sut.DiscordIntegration, Is.Null);
	}

	private static SettingsViewModel CreateSettings(AutoMocker mocker) =>
		new(mocker.Get<ISettingsService>(), mocker.Get<IUiDispatcher>());

	private static AutoMocker CreateMocker()
	{
		var mocker = new AutoMocker(MockBehavior.Strict);
		mocker.GetMock<IUiDispatcher>().Setup(x => x.Post(It.IsAny<Action>())).Callback<Action>(action => action());
		return mocker;
	}
}
