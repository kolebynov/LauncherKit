using Avalonia.Controls.Notifications;
using LauncherKit.Core.Feed;
using LauncherKit.Core.Feed.Mods;
using LauncherKit.Core.Models;
using LauncherKit.Core.Services.Abstractions;
using LauncherKit.Desktop.Localization;
using LauncherKit.Desktop.Services;
using LauncherKit.Infrastructure;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NuGet.Versioning;

namespace LauncherKit.Desktop.Tests.Services;

[TestFixture]
public sealed class ModUpdateNotificationsTests
{
	private const string Title = "Mod updates";

	[Test]
	public async Task Start_InstalledModsWithUpdates_ShowsOneNotificationWithDisplayNamesInFeedOrder()
	{
		var mocker = CreateMocker();
		var graphics = CreateMod("Graphics", new LocalizedText { ["ru"] = "Графика" });
		var current = CreateMod("Current");
		var notInstalled = CreateMod("NotInstalled");
		var voices = CreateMod("Voices");
		SetupFeed(mocker, graphics, current, notInstalled, voices);
		SetupState(mocker, graphics, ModInstallState.Installed(isUpdateAvailable: true, isUninstallSupported: false));
		SetupState(mocker, current, ModInstallState.Installed(isUpdateAvailable: false, isUninstallSupported: true));
		SetupState(mocker, notInstalled, ModInstallState.NotInstalled);
		SetupState(mocker, voices, ModInstallState.Installed(isUpdateAvailable: true, isUninstallSupported: true));
		var shown = CaptureShown(mocker);
		using var sut = mocker.CreateInstance<ModUpdateNotifications>();

		await sut.Start();

		Assert.That(shown, Has.Count.EqualTo(1));
		Assert.Multiple(() =>
		{
			Assert.That(shown[0].Type, Is.EqualTo(NotificationType.Information));
			Assert.That(shown[0].Title, Is.EqualTo(Title));
			Assert.That(shown[0].Message, Is.EqualTo("Графика\nVoices"));
			Assert.That(shown[0].IsInProgress, Is.False);
		});
	}

	[Test]
	public async Task Start_NoUpdates_ShowsNothing()
	{
		var mocker = CreateMocker();
		var current = CreateMod("Current");
		var notInstalled = CreateMod("NotInstalled");
		SetupFeed(mocker, current, notInstalled);
		SetupState(mocker, current, ModInstallState.Installed(isUpdateAvailable: false, isUninstallSupported: false));
		SetupState(mocker, notInstalled, ModInstallState.NotInstalled);
		using var sut = mocker.CreateInstance<ModUpdateNotifications>();

		await sut.Start();

		mocker.GetMock<INotificationService>().Verify(x => x.Show(It.IsAny<AppNotification>()), Times.Never);
	}

	[Test]
	public async Task Start_ModpackNotInstalled_DoesNotLoadFeed()
	{
		var mocker = CreateMocker(modpackInstalled: false);
		using var sut = mocker.CreateInstance<ModUpdateNotifications>();

		await sut.Start();

		mocker.GetMock<IFeedProvider>().Verify(x => x.GetModFeedDatas(It.IsAny<CancellationToken>()), Times.Never);
	}

	[Test]
	public async Task Start_CalledAgain_ChecksAndNotifiesOnce()
	{
		var mocker = CreateMocker();
		var mod = CreateMod("Mod");
		SetupFeed(mocker, mod);
		SetupState(mocker, mod, ModInstallState.Installed(isUpdateAvailable: true, isUninstallSupported: false));
		var shown = CaptureShown(mocker);
		using var sut = mocker.CreateInstance<ModUpdateNotifications>();

		await sut.Start();
		await sut.Start();

		Assert.That(shown, Has.Count.EqualTo(1));
		mocker.GetMock<IFeedProvider>().Verify(x => x.GetModFeedDatas(It.IsAny<CancellationToken>()), Times.Once);
	}

	[Test]
	public async Task Start_AfterDispose_DoesNotCheck()
	{
		var mocker = CreateMocker();
		var sut = mocker.CreateInstance<ModUpdateNotifications>();
		sut.Dispose();

		await sut.Start();

		mocker.GetMock<IModpackVersionProvider>().VerifyGet(x => x.InstalledModpackVersion, Times.Never);
		mocker.GetMock<IFeedProvider>().Verify(x => x.GetModFeedDatas(It.IsAny<CancellationToken>()), Times.Never);
	}

	[Test]
	public async Task Dispose_WhileFeedLoads_CancelsWithoutNotification()
	{
		var mocker = CreateMocker();
		var loadStarted = new TaskCompletionSource();
		mocker.GetMock<IFeedProvider>().Setup(x => x.GetModFeedDatas(It.IsAny<CancellationToken>()))
			.Returns(async (CancellationToken cancellationToken) =>
			{
				loadStarted.SetResult();
				await Task.Delay(Timeout.Infinite, cancellationToken);
				return (IReadOnlyList<ModFeedData>)[];
			});
		var sut = mocker.CreateInstance<ModUpdateNotifications>();
		var check = sut.Start();
		await loadStarted.Task;

		sut.Dispose();
		await check;

		mocker.GetMock<INotificationService>().Verify(x => x.Show(It.IsAny<AppNotification>()), Times.Never);
	}

	[Test]
	public async Task Dispose_BeforeQueuedNotificationRuns_DropsNotification()
	{
		var mocker = CreateMocker();
		var dispatcher = new QueuedUiDispatcher();
		mocker.Use<IUiDispatcher>(dispatcher);
		var mod = CreateMod("Mod");
		SetupFeed(mocker, mod);
		SetupState(mocker, mod, ModInstallState.Installed(isUpdateAvailable: true, isUninstallSupported: false));
		var sut = mocker.CreateInstance<ModUpdateNotifications>();
		await sut.Start();

		sut.Dispose();
		dispatcher.RunAll();

		mocker.GetMock<INotificationService>().Verify(x => x.Show(It.IsAny<AppNotification>()), Times.Never);
	}

	[Test]
	public async Task Start_FeedFails_ShowsNothingAndCompletes()
	{
		var mocker = CreateMocker();
		mocker.GetMock<IFeedProvider>().Setup(x => x.GetModFeedDatas(It.IsAny<CancellationToken>()))
			.ThrowsAsync(new InvalidOperationException("feed"));
		using var sut = mocker.CreateInstance<ModUpdateNotifications>();

		await sut.Start();

		mocker.GetMock<INotificationService>().Verify(x => x.Show(It.IsAny<AppNotification>()), Times.Never);
	}

	private static AutoMocker CreateMocker(bool modpackInstalled = true)
	{
		var mocker = new AutoMocker(MockBehavior.Strict);
		mocker.Use<IUiDispatcher>(ImmediateUiDispatcher.Instance);
		mocker.Use<ILogger<ModUpdateNotifications>>(NullLogger<ModUpdateNotifications>.Instance);
		mocker.GetMock<IModpackVersionProvider>().SetupGet(x => x.InstalledModpackVersion)
			.Returns(modpackInstalled ? new NuGetVersion(3, 3, 0) : null);
		mocker.GetMock<IStringLocalizer<DesktopStrings>>().Setup(x => x[DesktopStrings.ModUpdatesAvailable])
			.Returns(new LocalizedString(DesktopStrings.ModUpdatesAvailable, Title));
		return mocker;
	}

	private static ModFeedData CreateMod(string name, LocalizedText? displayName = null) => new()
	{
		Name = name,
		CategoryId = "mods",
		DisplayName = displayName,
		LogoPath = "",
		DescriptionPath = new LocalizedText { ["ru"] = "" },
		Items = [],
	};

	private static void SetupFeed(AutoMocker mocker, params ModFeedData[] mods) =>
		mocker.GetMock<IFeedProvider>().Setup(x => x.GetModFeedDatas(It.IsAny<CancellationToken>()))
			.ReturnsAsync(mods);

	private static void SetupState(AutoMocker mocker, ModFeedData mod, ModInstallState state) =>
		mocker.GetMock<IInstallationService>().Setup(x => x.GetModInstallState(mod)).Returns(state);

	private static List<AppNotification> CaptureShown(AutoMocker mocker)
	{
		var shown = new List<AppNotification>();
		mocker.GetMock<INotificationService>().Setup(x => x.Show(It.IsAny<AppNotification>())).Callback(shown.Add);
		return shown;
	}

	private sealed class QueuedUiDispatcher : IUiDispatcher
	{
		private readonly List<Action> _actions = [];

		public void Post(Action action) => _actions.Add(action);

		public void RunAll() => _actions.ForEach(x => x());
	}
}
