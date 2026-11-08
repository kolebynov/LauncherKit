using LauncherKit.Desktop.Services;
using LauncherKit.Desktop.ViewModels;
using LauncherKit.Infrastructure;
using R3;
using LauncherKit.Core.AppTasks;
using LauncherKit.Core.AppTasks.MicroTasks;
using LauncherKit.Core.Choices;

namespace LauncherKit.Desktop.Tests.ViewModels;

[TestFixture]
public sealed class AppTaskViewModelTests
{
	[Test]
	public void SetRunningMicroTask_MicroOperation_ProjectsNamedProgressThenErrorClearsIt()
	{
		// Arrange
		var mocker = new AutoMocker(MockBehavior.Strict);
		mocker.Use<IUiDispatcher>(ImmediateDispatcher.Instance);
		mocker.Use(new AppTask("task", new AppTaskDescription.Custom("Task"), _ => [], [], null));
		var sut = mocker.CreateInstance<AppTaskViewModel>();

		// Act
		sut.SetRunningMicroTask(new MicroTaskDescription.DownloadFile("archive.zip"), Observable.Return<MicroTaskStep>(new MicroTaskStep.MicroOperation(MicroTaskDescription.Preparing.Instance)));
		var progress = sut.ProgressViewModel;
		var failure = new InvalidOperationException("failed");
		sut.SetError(failure);

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(progress, Is.TypeOf<UnknownProgressViewModel>());
			Assert.That(((UnknownProgressViewModel)progress!).Operation?.Value, Is.EqualTo(MicroTaskDescription.Preparing.Instance));
			Assert.That(sut.ProgressViewModel, Is.Null);
			Assert.That(sut.Error, Is.SameAs(failure));
		});
	}

	[Test]
	public void SelectChoicesStep_ProjectsSelectionAndCompletionClearsIt()
	{
		var mocker = new AutoMocker(MockBehavior.Strict);
		mocker.Use<IUiDispatcher>(ImmediateDispatcher.Instance);
		mocker.Use(new AppTask("task", new AppTaskDescription.Custom("Task"), _ => [], [], null));
		var sut = mocker.CreateInstance<AppTaskViewModel>();
		using var context = new ChoiceSelectionContext(new ChoiceText.Content("Choices"), [], CancellationToken.None);
		sut.SetRunningMicroTask(MicroTaskDescription.Preparing.Instance, Observable.Return<MicroTaskStep>(new MicroTaskStep.SelectChoices(context)));
		var selection = sut.ChoiceSelection;
		selection!.GoNext();
		sut.Complete();
		Assert.Multiple(() =>
		{
			Assert.That(selection.Title, Is.EqualTo(context.Title));
			Assert.That(context.ResultTask.IsCompletedSuccessfully, Is.True);
			Assert.That(sut.ChoiceSelection, Is.Null);
			Assert.That(sut.ProgressViewModel, Is.Null);
		});
	}

	[Test]
	public void ReplacingMicroTask_UnsubscribesOldChoiceSteps()
	{
		var mocker = new AutoMocker(MockBehavior.Strict);
		mocker.Use<IUiDispatcher>(ImmediateDispatcher.Instance);
		mocker.Use(new AppTask("task", new AppTaskDescription.Custom("Task"), _ => [], [], null));
		var sut = mocker.CreateInstance<AppTaskViewModel>();
		using var steps = new Subject<MicroTaskStep>();
		using var context = new ChoiceSelectionContext(new ChoiceText.Content("Choices"), [], CancellationToken.None);
		sut.SetRunningMicroTask(MicroTaskDescription.Preparing.Instance, steps);
		sut.Complete();
		steps.OnNext(new MicroTaskStep.SelectChoices(context));
		Assert.That(sut.ChoiceSelection, Is.Null);
	}

	private sealed class ImmediateDispatcher : IUiDispatcher
	{
		public void Post(Action action) => action();
	}
}
