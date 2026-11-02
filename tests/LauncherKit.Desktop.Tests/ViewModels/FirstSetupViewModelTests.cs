using LauncherKit.Desktop.Services;
using LauncherKit.Desktop.ViewModels;
using LauncherKit.Core.Services.Abstractions;
using LauncherKit.Core.UserSettings;
using LauncherKit.Core.Utils;
using LauncherKit.TestSupport;
using LauncherKit.Infrastructure;

namespace LauncherKit.Desktop.Tests.ViewModels;

[TestFixture]
public sealed class FirstSetupViewModelTests
{
	[Test]
	public async Task Start_FirstCall_ShowsWelcomeStepAndSecondCallIsIdempotent()
	{
		var mocker = CreateMocker(new AppSettings());
		var sut = mocker.CreateInstance<FirstSetupViewModel>();
		await sut.Start();
		await sut.Start();
		Assert.That(sut.Step, Is.EqualTo(FirstSetupStep.Welcome));
		Assert.That(sut.State, Is.EqualTo(FirstSetupState.InProgress));
	}

	[Test]
	public async Task AppDataDirectoryStep_NestedDirectory_BlocksContinue()
	{
		using var skyrim = new TempDirectory();
		var mocker = CreateMocker(new AppSettings { SkyrimDirectory = skyrim.Path });
		var sut = mocker.CreateInstance<FirstSetupViewModel>();
		await sut.Start();
		await sut.ContinueCommand.ExecuteAsync(null);
		sut.AppDataDirectory.SelectedDirectory = skyrim.Combine("LauncherData");
		Assert.That(sut.Step, Is.EqualTo(FirstSetupStep.AppDataDirectory));
		Assert.That(sut.AppDataDirectory.GetErrors(null), Is.EqualTo(new[] { DirectoryValidationError.AppDataDirectoryConflict }));
		Assert.That(sut.ContinueCommand.CanExecute(null), Is.False);
	}

	[Test]
	public async Task Complete_SavesAndReloadsConfiguration()
	{
		using var skyrim = new TempDirectory();
		using var data = new TempDirectory();
		var mocker = CreateMocker(new AppSettings { SkyrimDirectory = skyrim.Path, AppDataDirectory = data.Path });
		mocker.GetMock<IFeedAvailabilityChecker>().Setup(x => x.CheckFeed()).ReturnsAsync(true);
		mocker.GetMock<ISettingsService>().Setup(x => x.SaveSettings(It.IsAny<AllSettings>())).Returns(Task.CompletedTask);
		mocker.GetMock<IAppReloader>().Setup(x => x.ReloadApp(AppReloadMode.Configuration));
		var sut = mocker.CreateInstance<FirstSetupViewModel>();
		await sut.Start();
		await sut.ContinueCommand.ExecuteAsync(null);
		await sut.Availability.CheckAvailabilityCommand.ExecuteAsync(null);
		await sut.Availability.FinishCommand.ExecuteAsync(null);
		Assert.That(sut.State, Is.EqualTo(FirstSetupState.Finished));
		mocker.GetMock<IAppReloader>().Verify(x => x.ReloadApp(AppReloadMode.Configuration), Times.Once);
	}

	[Test]
	public async Task SkyrimStep_UnsupportedVersion_ExposesErrorAndDoesNotAdvance()
	{
		using var directory = new TempDirectory();
		var detected = new Version(0, 1);
		var mocker = CreateMocker(new AppSettings());
		mocker.GetMock<ISkyrimInstallationChecker>().Setup(x => x.CheckSkyrimDirectory(directory.Path))
			.Returns(new SkyrimDirectoryCheckResult.SkyrimDirectory(detected));
		var sut = mocker.CreateInstance<FirstSetupViewModel>();
		await sut.Start();
		await sut.ContinueCommand.ExecuteAsync(null);
		sut.SkyrimDirectory.SelectedDirectory = directory.Path;
		await sut.ContinueCommand.ExecuteAsync(null);
		Assert.That(sut.Step, Is.EqualTo(FirstSetupStep.SkyrimDirectory));
		Assert.That(sut.Error!.DetectedSkyrimVersion, Is.EqualTo(detected));
	}

	[Test]
	public async Task SkyrimStep_FirstValidDirectory_NotifiesContinueAvailabilityWithoutValidationErrorTransition()
	{
		using var directory = new TempDirectory();
		var sut = CreateMocker(new AppSettings()).CreateInstance<FirstSetupViewModel>();
		await sut.Start();
		await sut.ContinueCommand.ExecuteAsync(null);
		Assert.That(sut.ContinueCommand.CanExecute(null), Is.False);
		var notified = false;
		sut.ContinueCommand.CanExecuteChanged += (_, _) => notified = true;

		sut.SkyrimDirectory.SelectedDirectory = directory.Path;

		Assert.That(notified, Is.True);
		Assert.That(sut.ContinueCommand.CanExecute(null), Is.True);
	}

	private static AutoMocker CreateMocker(AppSettings app)
	{
		var mocker = ProductTestSupport.CreateMocker();
		mocker.GetMock<IUiDispatcher>().Setup(x => x.Post(It.IsAny<Action>())).Callback<Action>(action => action());
		mocker.GetMock<ISkyrimDirectoryFinder>().Setup(x => x.FindSkyrimDirectory()).Returns((string?)null);
		mocker.GetMock<ISettingsService>().Setup(x => x.GetSettings()).Returns(new AllSettings
		{
			App = app, Download = new DownloadSettings(), Window = new WindowSettings(),
		});
		return mocker;
	}
}
