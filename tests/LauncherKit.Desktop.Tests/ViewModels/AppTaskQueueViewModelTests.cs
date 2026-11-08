using LauncherKit.Desktop.Services;
using LauncherKit.Desktop.ViewModels;
using LauncherKit.Infrastructure;
using R3;
using LauncherKit.Core.AppTasks;
using LauncherKit.Core.AppTasks.MicroTasks;
using LauncherKit.Core.Choices;

namespace LauncherKit.Desktop.Tests.ViewModels;

[TestFixture]
public sealed class AppTaskQueueViewModelTests
{
	[Test]
	public void QueueEvents_QueuedThenCompleted_AddsThenRemovesTask()
	{
		// Arrange
		using var events = new Subject<AppTaskQueue.Event>();
		var mocker = new AutoMocker(MockBehavior.Strict);
		mocker.Use<IUiDispatcher>(ImmediateDispatcher.Instance);
		mocker.GetMock<IAppTaskQueue>().SetupGet(x => x.Events).Returns(events);
		using var sut = mocker.CreateInstance<AppTaskQueueViewModel>();
		var task = new AppTask("install", new AppTaskDescription.Custom("Install"), _ => [], [], null);

		// Act
		events.OnNext(new AppTaskQueue.Event.TaskQueued(task));
		var countAfterQueued = sut.AppTasks.Count;
		events.OnNext(new AppTaskQueue.Event.TaskCompleted(task));

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(countAfterQueued, Is.EqualTo(1));
			Assert.That(sut.AppTasks, Is.Empty);
		});
	}

	[Test]
	public void QueueRemoval_ClearsActiveChoiceSelectionBeforeRemovingProjection()
	{
		using var events = new Subject<AppTaskQueue.Event>();
		var mocker = new AutoMocker(MockBehavior.Strict);
		mocker.Use<IUiDispatcher>(ImmediateDispatcher.Instance);
		mocker.GetMock<IAppTaskQueue>().SetupGet(x => x.Events).Returns(events);
		using var sut = mocker.CreateInstance<AppTaskQueueViewModel>();
		var task = new AppTask("install", new AppTaskDescription.Custom("Install"), _ => [], [], null);
		events.OnNext(new AppTaskQueue.Event.TaskQueued(task));
		var projection = sut.AppTasks.Single();
		using var context = new ChoiceSelectionContext(new ChoiceText.Content("Choices"), [], CancellationToken.None);
		projection.SetRunningMicroTask(MicroTaskDescription.Preparing.Instance, Observable.Return<MicroTaskStep>(new MicroTaskStep.SelectChoices(context)));
		events.OnNext(new AppTaskQueue.Event.TaskRemoved(task, 0));
		Assert.Multiple(() =>
		{
			Assert.That(sut.AppTasks, Is.Empty);
			Assert.That(projection.ChoiceSelection, Is.Null);
		});
	}

	private sealed class ImmediateDispatcher : IUiDispatcher
	{
		public void Post(Action action) => action();
	}
}
