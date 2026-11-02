using LauncherKit.Desktop.ViewModels;
using LauncherKit.Core.Choices;

namespace LauncherKit.Desktop.Tests.ViewModels;

[TestFixture]
public sealed class ChoiceSelectionViewModelTests
{
	[Test]
	public void GoNext_FirstStepSatisfied_AdvancesToUnconfiguredStep()
	{
		// Arrange
		using var context = new ChoiceSelectionContext(new ChoiceText.Content("Title"), CreateSteps(), CancellationToken.None);
		var sut = new ChoiceSelectionViewModel(context);
		var canAdvanceInitially = sut.CanGoNext;
		var initialStep = sut.CurrentStep;

		// Act
		sut.GoNext();

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(canAdvanceInitially, Is.True);
			Assert.That(sut.CurrentStep, Is.Not.SameAs(initialStep));
			Assert.That(sut.CanGoNext, Is.False);
			Assert.That(sut.FullyConfigured, Is.False);
		});
	}

	[Test]
	public void GetSelections_OptionsSelected_ReturnsEveryStepWithItsSelectedOptionsInOrder()
	{
		// Arrange
		var steps = CreateSteps();
		using var context = new ChoiceSelectionContext(new ChoiceText.Content("Title"), steps, CancellationToken.None);
		var sut = new ChoiceSelectionViewModel(context);
		sut.AllSteps[1].Options[1].IsSelected = true;

		// Act
		var selections = sut.GetSelections();

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(selections.Select(x => x.Step), Is.EqualTo(steps));
			Assert.That(selections[0].SelectedOptions, Is.EqualTo(new[] { steps[0].Options[0] }));
			Assert.That(selections[1].SelectedOptions, Is.EqualTo(new[] { steps[1].Options[1] }));
		});
	}

	[Test]
	public async Task Navigation_BackThenFinish_CompletesOriginalContextWithRetainedSelections()
	{
		using var context = new ChoiceSelectionContext(new ChoiceText.Content("Title"), CreateSteps(), CancellationToken.None);
		var sut = new ChoiceSelectionViewModel(context);
		sut.GoNext();
		sut.CurrentStep!.Options[1].IsSelected = true;
		sut.GoBack();
		Assert.That(sut.CanGoBack, Is.False);
		sut.GoNext();
		Assert.That(sut.CurrentStep!.Options[1].IsSelected, Is.True);
		sut.GoNext();
		var result = await context.ResultTask;
		Assert.Multiple(() =>
		{
			Assert.That(sut.IsFinished, Is.True);
			Assert.That(sut.GoNextCommand.CanExecute(null), Is.False);
			Assert.That(result[1].SelectedOptions, Is.EqualTo(new[] { context.Steps[1].Options[1] }));
		});
	}

	[Test]
	public async Task EmptySteps_Finish_CompletesWithEmptySelections()
	{
		using var context = new ChoiceSelectionContext(new ChoiceText.Content("Title"), [], CancellationToken.None);
		var sut = new ChoiceSelectionViewModel(context);
		sut.GoNext();
		Assert.That(await context.ResultTask, Is.Empty);
	}

	[Test]
	public void CancelledContext_ResultRemainsCancelled()
	{
		using var cancellation = new CancellationTokenSource();
		using var context = new ChoiceSelectionContext(new ChoiceText.Content("Title"), [], cancellation.Token);
		var sut = new ChoiceSelectionViewModel(context);
		cancellation.Cancel();
		sut.GoNext();
		Assert.That(context.ResultTask.IsCanceled, Is.True);
	}

	private static ChoiceStep[] CreateSteps() =>
	[
		new()
		{
			Title = new ChoiceText.Content("Start"),
			Subtitle = new ChoiceText.Content(string.Empty),
			SelectionMode = ChoiceSelectionMode.Any,
			Options = [new ChoiceOption { Name = new ChoiceText.Content("Required"), Description = new ChoiceText.Content(string.Empty), IsRequired = true }],
		},
		new()
		{
			Title = new ChoiceText.Content("Weather"),
			Subtitle = new ChoiceText.Content("Pick one"),
			SelectionMode = ChoiceSelectionMode.ExactlyOne,
			Options =
			[
				new ChoiceOption { Name = new ChoiceText.Content("A"), Description = new ChoiceText.Content(string.Empty) },
				new ChoiceOption { Name = new ChoiceText.Content("B"), Description = new ChoiceText.Content(string.Empty) },
			],
		},
	];
}
