using Microsoft.Extensions.Logging.Abstractions;
using LauncherKit.Desktop.Services;
using LauncherKit.Core.AppTasks;
using LauncherKit.Core.Services.Abstractions;
using LauncherKit.Core.UserSettings;
using LauncherKit.Infrastructure;

namespace LauncherKit.Desktop.Avalonia.Tests.Services;

[TestFixture]
public sealed class AppCloseCoordinatorTests
{
	private static AppCloseContext Context(bool confirmed = false) =>
		new(new AppWindowSnapshot(AppWindowState.Normal, 1100, 750, 30, 40), confirmed);

	[TestCase(0, false, true)]
	[TestCase(1, false, false)]
	[TestCase(1, true, true)]
	public async Task PendingTasks_UsesOnlyQueueFactsAndConsumerDecision(int taskCount, bool confirmed, bool canClose)
	{
		var queue = new Mock<IAppTaskQueue>();
		queue.SetupGet(x => x.TaskCount).Returns(taskCount);
		var result = await new PendingTasksCloseHandler(queue.Object).Handle(Context(confirmed));
		Assert.That(result is IAppCloseHandler.HandleResult.CanClose, Is.EqualTo(canClose));
	}

	[Test]
	public async Task Coordinator_VetoStopsPipelineAndAllowsRetry()
	{
		var allowed = false;
		var first = new Mock<IAppCloseHandler>();
		first.Setup(x => x.Handle(It.IsAny<AppCloseContext>())).Returns(() => new ValueTask<IAppCloseHandler.HandleResult>(
			allowed ? new IAppCloseHandler.HandleResult.CanClose() : new IAppCloseHandler.HandleResult.CannotClose()));
		var second = new Mock<IAppCloseHandler>();
		second.Setup(x => x.Handle(It.IsAny<AppCloseContext>())).ReturnsAsync(new IAppCloseHandler.HandleResult.CanClose());
		var sut = new AppCloseCoordinator(() => [first.Object, second.Object], NullLogger.Instance);

		Assert.That(await sut.TryClose(Context()), Is.False);
		Assert.That(sut.IsClosing, Is.False);
		second.Verify(x => x.Handle(It.IsAny<AppCloseContext>()), Times.Never);
		allowed = true;
		Assert.That(await sut.TryClose(Context()), Is.True);
		Assert.That(await sut.TryClose(Context()), Is.True);
		second.Verify(x => x.Handle(It.IsAny<AppCloseContext>()), Times.Once);
	}

	[Test]
	public async Task Coordinator_RepeatedRequestDoesNotInvokeHandlersAgainWhileAwaiting()
	{
		var completion = new TaskCompletionSource<IAppCloseHandler.HandleResult>();
		var handler = new Mock<IAppCloseHandler>();
		handler.Setup(x => x.Handle(It.IsAny<AppCloseContext>())).Returns(() => new(completion.Task));
		var sut = new AppCloseCoordinator(() => [handler.Object], NullLogger.Instance);
		var pending = sut.TryClose(Context());
		Assert.That(await sut.TryClose(Context()), Is.False);
		completion.SetResult(new IAppCloseHandler.HandleResult.CanClose());
		Assert.That(await pending, Is.True);
		handler.Verify(x => x.Handle(It.IsAny<AppCloseContext>()), Times.Once);
	}

	[Test]
	public async Task Coordinator_HandlerFailureIsLoggedAndLaterHandlersStillRun()
	{
		var failed = new Mock<IAppCloseHandler>();
		failed.Setup(x => x.Handle(It.IsAny<AppCloseContext>())).ThrowsAsync(new InvalidOperationException("failed"));
		var next = new Mock<IAppCloseHandler>();
		next.Setup(x => x.Handle(It.IsAny<AppCloseContext>())).ReturnsAsync(new IAppCloseHandler.HandleResult.CanClose());
		var sut = new AppCloseCoordinator(() => [failed.Object, next.Object], NullLogger.Instance);

		Assert.That(await sut.TryClose(Context()), Is.True);
		next.Verify(x => x.Handle(It.IsAny<AppCloseContext>()), Times.Once);
	}

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
	public async Task SaveWindowState_PreservesNormalBoundsWhenMaximizedOrMinimized(AppWindowState state)
	{
		var settings = new AllSettings
		{
			App = new AppSettings(), Download = new DownloadSettings(),
			Window = new WindowSettings { Width = 900, Height = 600, PositionX = 10, PositionY = 20 },
		};
		var service = new Mock<ISettingsService>();
		service.Setup(x => x.ModifySettings(It.IsAny<Func<AllSettings, ValueTask>>()))
			.Returns(async (Func<AllSettings, ValueTask> modify) => await modify(settings));
		var snapshot = Context().Window with { State = state };

		service.Setup(x => x.GetSettings()).Returns(settings);
		var windowState = new WindowStateService(service.Object);
		await new SaveWindowStateCloseHandler(windowState).Handle(new(snapshot, false));
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
