using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using LauncherKit.Core.AppTasks;
using LauncherKit.Desktop.Avalonia.Services;
using LauncherKit.Desktop.Localization;
using LauncherKit.Desktop.Services;
using Microsoft.Extensions.Localization;

namespace LauncherKit.Desktop.Avalonia.Tests.Services;

[NonParallelizable]
[TestFixture]
public sealed class PendingTasksCloseHandlerTests
{
	[AvaloniaTest]
	public async Task Handle_NoPendingTasks_CanCloseWithoutAsking()
	{
		// Arrange
		var mocker = CreateMocker(taskCount: 0);

		// Act
		var result = await mocker.CreateInstance<PendingTasksCloseHandler>().Handle(new Window());

		// Assert
		Assert.That(result.Value, Is.InstanceOf<IAppCloseHandler.HandleResult.CanClose>());
	}

	[AvaloniaTest]
	[TestCase(true, typeof(IAppCloseHandler.HandleResult.CanClose))]
	[TestCase(false, typeof(IAppCloseHandler.HandleResult.CannotClose))]
	public async Task Handle_PendingTasks_AnswerDecides(bool confirmed, Type expected)
	{
		// Arrange
		var mocker = CreateMocker(taskCount: 1);
		mocker.GetMock<IDialogService>().Setup(x => x.ShowYesNoConfirmation(DesktopStrings.PendingTasksCloseTitle,
			DesktopStrings.PendingTasksCloseMessage, true, It.IsAny<CancellationToken>())).ReturnsAsync(confirmed);

		// Act
		var result = await mocker.CreateInstance<PendingTasksCloseHandler>().Handle(new Window());

		// Assert
		Assert.That(result.Value, Is.InstanceOf(expected));
	}

	[AvaloniaTest]
	public async Task Handle_TasksFinishWhileAsking_ClosesQuestionAndCanClose()
	{
		// Arrange
		var mocker = CreateMocker(taskCount: 1);
		var queue = mocker.GetMock<IAppTaskQueue>();
		var questionToken = CancellationToken.None;
		mocker.GetMock<IDialogService>().Setup(x => x.ShowYesNoConfirmation(DesktopStrings.PendingTasksCloseTitle,
				DesktopStrings.PendingTasksCloseMessage, true, It.IsAny<CancellationToken>()))
			.Returns<string, string, bool, CancellationToken>((_, _, _, token) =>
			{
				questionToken = token;
				var answer = new TaskCompletionSource<bool>();
				token.Register(() => answer.SetResult(false));
				return answer.Task;
			});
		var pending = mocker.CreateInstance<PendingTasksCloseHandler>().Handle(new Window());
		Assert.That(pending.IsCompleted, Is.False);

		// Act
		queue.SetupGet(x => x.TaskCount).Returns(0);
		queue.Raise(x => x.PropertyChanged += null, new PropertyChangedEventArgs(nameof(IAppTaskQueue.TaskCount)));
		var result = await pending;

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(questionToken.IsCancellationRequested, Is.True);
			Assert.That(result.Value, Is.InstanceOf<IAppCloseHandler.HandleResult.CanClose>());
		});
	}

	private static AutoMocker CreateMocker(int taskCount)
	{
		var mocker = new AutoMocker(MockBehavior.Strict);
		var queue = mocker.GetMock<IAppTaskQueue>();
		queue.SetupGet(x => x.TaskCount).Returns(taskCount);
		queue.SetupRemove(x => x.PropertyChanged -= It.IsAny<PropertyChangedEventHandler>());
		mocker.GetMock<IStringLocalizer<DesktopStrings>>().Setup(x => x[It.IsAny<string>()])
			.Returns<string>(key => new LocalizedString(key, key));
		mocker.Use<IUiDispatcher>(new ImmediateUiDispatcher());
		return mocker;
	}
}
