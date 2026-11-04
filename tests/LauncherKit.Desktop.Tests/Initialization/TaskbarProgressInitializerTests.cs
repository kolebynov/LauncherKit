using R3;
using LauncherKit.Desktop.Initialization;
using LauncherKit.Desktop.Interop;
using LauncherKit.Desktop.Services;
using LauncherKit.Core.AppTasks;

namespace LauncherKit.Desktop.Tests.Initialization;

[TestFixture]
public sealed class TaskbarProgressInitializerTests
{
	[Test]
	public async Task Initialize_TaskFailure_SetsErrorThenDisposeClearsState()
	{
		// Arrange
		using var events = new Subject<AppTaskQueue.Event>();
		var mocker = new AutoMocker(MockBehavior.Strict);
		mocker.GetMock<IAppTaskQueue>().SetupGet(x => x.Events).Returns(events);
		var sink = mocker.GetMock<ITaskbarProgressSink>();
		sink.Setup(x => x.SetProgress(TaskbarProgressBarState.Error, 1000, 1000));
		sink.Setup(x => x.SetProgress(TaskbarProgressBarState.NoProgress, 0, 1000));
		var sut = mocker.CreateInstance<TaskbarProgressInitializer>();
		var task = new AppTask("failed", new AppTaskDescription.Custom("failed"), _ => [], [], null);

		// Act
		await sut.Initialize();
		events.OnNext(new AppTaskQueue.Event.TaskFailed(task, new InvalidOperationException("failed")));
		sut.Dispose();

		// Assert
		sink.Verify(x => x.SetProgress(TaskbarProgressBarState.Error, 1000, 1000), Times.Once);
		sink.Verify(x => x.SetProgress(TaskbarProgressBarState.NoProgress, 0, 1000), Times.Once);
	}
}
