using LauncherKit.Desktop.Services;
using LauncherKit.Core.Services.Abstractions;
using LauncherKit.Core.UserSettings;
using LauncherKit.Infrastructure;

namespace LauncherKit.Desktop.Avalonia.Tests.Services;

[TestFixture]
public sealed class WindowStateServiceTests
{
	[Test]
	public void WindowState_RestoresConfiguredBoundsAndMaximizedState()
	{
		var settings = new AllSettings
		{
			App = new AppSettings(), Download = new DownloadSettings(),
			Window = new WindowSettings { Width = 1234, Height = 789, PositionX = -100, PositionY = 75, IsMaximized = true },
		};
		var service = new Mock<ISettingsService>();
		service.Setup(x => x.GetSettings()).Returns(settings);

		Assert.That(new WindowStateService(service.Object).Restore(),
			Is.EqualTo(new AppWindowSnapshot(AppWindowState.Maximized, 1234, 789, -100, 75)));
	}

	[TestCase(AppWindowState.Normal)]
	[TestCase(AppWindowState.Maximized)]
	[TestCase(AppWindowState.Minimized)]
	public async Task Save_PreservesNormalBoundsWhenMaximizedOrMinimized(AppWindowState state)
	{
		var settings = new AllSettings
		{
			App = new AppSettings(), Download = new DownloadSettings(),
			Window = new WindowSettings { Width = 900, Height = 600, PositionX = 10, PositionY = 20 },
		};
		var service = new Mock<ISettingsService>();
		service.Setup(x => x.ModifySettings(It.IsAny<Func<AllSettings, ValueTask>>()))
			.Returns(async (Func<AllSettings, ValueTask> modify) => await modify(settings));

		service.Setup(x => x.GetSettings()).Returns(settings);
		var windowState = new WindowStateService(service.Object);
		await windowState.Save(new AppWindowSnapshot(state, 1100, 750, 30, 40));
		var restored = windowState.Restore();

		Assert.Multiple(() =>
		{
			Assert.That(restored.Width, Is.EqualTo(state == AppWindowState.Normal ? 1100 : 900));
			Assert.That(restored.Height, Is.EqualTo(state == AppWindowState.Normal ? 750 : 600));
			Assert.That(restored.PositionX, Is.EqualTo(state == AppWindowState.Normal ? 30 : 10));
			Assert.That(restored.PositionY, Is.EqualTo(state == AppWindowState.Normal ? 40 : 20));
			Assert.That(restored.State, Is.EqualTo(state == AppWindowState.Maximized ? AppWindowState.Maximized : AppWindowState.Normal));
		});
		if (state == AppWindowState.Minimized)
		{
			service.Verify(x => x.ModifySettings(It.IsAny<Func<AllSettings, ValueTask>>()), Times.Never);
		}
	}
}
