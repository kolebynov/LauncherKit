using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using LauncherKit.Core.Services;
using LauncherKit.Core.Services.Abstractions;
using LauncherKit.Core.UserSettings;
using LauncherKit.Infrastructure;
using LauncherKit.TestSupport;

namespace LauncherKit.Core.Tests.Services;

[TestFixture]
public sealed class SettingsServiceTests
{
	[Test]
	public async Task SaveSettings_StoreReloadsOptions_PersistsJsonAndUpdatesCurrentSettings()
	{
		// Arrange
		var mocker = new AutoMocker(MockBehavior.Strict);
		mocker.Use<TimeProvider>(TimeProvider.System);
		mocker.Use<ILogger<SettingsService>>(NullLogger<SettingsService>.Instance);
		var initialApp = new AppSettings { AppDataDirectory = "old" };
		var appMonitor = OptionsStubs.Monitor(mocker, initialApp);
		OptionsStubs.Monitor(mocker, new DownloadSettings());
		OptionsStubs.Monitor(mocker, new WindowSettings());
		var updated = new AllSettings
		{
			App = new AppSettings { AppDataDirectory = "new" },
			Download = new DownloadSettings(),
			Window = new WindowSettings(),
		};
		string? writtenJson = null;
		mocker.GetMock<ISettingsFileStore>()
			.Setup(x => x.Write(It.IsAny<string>(), CancellationToken.None))
			.Callback<string, CancellationToken>((json, _) =>
			{
				writtenJson = json;
				appMonitor.Change(updated.App);
			})
			.Returns(Task.CompletedTask);
		var sut = mocker.CreateInstance<SettingsService>();

		// Act
		await sut.SaveSettings(updated);

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(sut.GetSettings(), Is.SameAs(updated));
			Assert.That(writtenJson, Does.Contain("new"));
		});
	}

	[Test]
	public async Task SaveSettings_StoreNeverReloadsOptions_CompletesAfterTimeoutAndUpdatesCurrentSettings()
	{
		// Arrange
		var mocker = new AutoMocker(MockBehavior.Strict);
		var timeProvider = new FakeTimeProvider();
		mocker.Use<TimeProvider>(timeProvider);
		mocker.Use<ILogger<SettingsService>>(NullLogger<SettingsService>.Instance);
		OptionsStubs.Monitor(mocker, new AppSettings());
		OptionsStubs.Monitor(mocker, new DownloadSettings());
		OptionsStubs.Monitor(mocker, new WindowSettings());
		var updated = new AllSettings
		{
			App = new AppSettings { AppDataDirectory = "new" },
			Download = new DownloadSettings(),
			Window = new WindowSettings(),
		};
		mocker.GetMock<ISettingsFileStore>()
			.Setup(x => x.Write(It.IsAny<string>(), CancellationToken.None))
			.Returns(Task.CompletedTask);
		var sut = mocker.CreateInstance<SettingsService>();

		// Act
		var saveTask = sut.SaveSettings(updated);
		var completedBeforeTimeout = saveTask.IsCompleted;
		timeProvider.Advance(TimeSpan.FromSeconds(1));
		await saveTask;

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(completedBeforeTimeout, Is.False);
			Assert.That(sut.GetSettings(), Is.SameAs(updated));
		});
	}

	[Test]
	public async Task ModifySettings_ChangesCurrentSettings_PersistsModifiedSettings()
	{
		// Arrange
		var mocker = new AutoMocker(MockBehavior.Strict);
		mocker.Use<TimeProvider>(TimeProvider.System);
		mocker.Use<ILogger<SettingsService>>(NullLogger<SettingsService>.Instance);
		var appMonitor = OptionsStubs.Monitor(mocker, new AppSettings());
		OptionsStubs.Monitor(mocker, new DownloadSettings { MaxDownloadSpeedBytesPerSecond = 0 });
		OptionsStubs.Monitor(mocker, new WindowSettings());
		string? writtenJson = null;
		mocker.GetMock<ISettingsFileStore>()
			.Setup(x => x.Write(It.IsAny<string>(), CancellationToken.None))
			.Callback<string, CancellationToken>((json, _) =>
			{
				writtenJson = json;
				appMonitor.Change(new AppSettings());
			})
			.Returns(Task.CompletedTask);
		using var sut = mocker.CreateInstance<SettingsService>();

		// Act
		await sut.ModifySettings(settings =>
		{
			settings.Download.MaxDownloadSpeedBytesPerSecond = 1024;
			return ValueTask.CompletedTask;
		});

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(sut.GetSettings().Download.MaxDownloadSpeedBytesPerSecond, Is.EqualTo(1024));
			Assert.That(writtenJson, Does.Contain("\"MaxDownloadSpeedBytesPerSecond\": 1024"));
		});
	}

	[Test]
	public async Task ModifySettings_ConcurrentWithSave_RunsAfterSaveCompletes()
	{
		// Arrange
		var mocker = new AutoMocker(MockBehavior.Strict);
		var timeProvider = new FakeTimeProvider();
		mocker.Use<TimeProvider>(timeProvider);
		mocker.Use<ILogger<SettingsService>>(NullLogger<SettingsService>.Instance);
		var appMonitor = OptionsStubs.Monitor(mocker, new AppSettings());
		OptionsStubs.Monitor(mocker, new DownloadSettings());
		OptionsStubs.Monitor(mocker, new WindowSettings());
		var writes = 0;
		mocker.GetMock<ISettingsFileStore>()
			.Setup(x => x.Write(It.IsAny<string>(), CancellationToken.None))
			.Callback<string, CancellationToken>((_, _) =>
			{
				// The first save never sees a reload, so it holds the lock until its timeout; later ones reload.
				if (++writes > 1)
				{
					appMonitor.Change(new AppSettings());
				}
			})
			.Returns(Task.CompletedTask);
		using var sut = mocker.CreateInstance<SettingsService>();
		var saved = new AllSettings
		{
			App = new AppSettings(),
			Download = new DownloadSettings(),
			Window = new WindowSettings(),
		};
		AllSettings? modified = null;

		// Act
		var saveTask = sut.SaveSettings(saved);
		var modifyTask = sut.ModifySettings(settings =>
		{
			modified = settings;
			return ValueTask.CompletedTask;
		});
		var modifiedDuringSave = modified != null;
		timeProvider.Advance(TimeSpan.FromSeconds(1));
		await saveTask;
		await modifyTask;

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(modifiedDuringSave, Is.False);
			Assert.That(modified, Is.SameAs(saved));
		});
	}
}
