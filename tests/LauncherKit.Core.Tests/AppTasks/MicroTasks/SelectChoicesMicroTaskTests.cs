using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using R3;
using LauncherKit.Core.AppTasks.MicroTasks;
using LauncherKit.Core.Choices;
using LauncherKit.Core.Feed;
using LauncherKit.Core.Feed.Install;
using LauncherKit.Core.Models;

namespace LauncherKit.Core.Tests.AppTasks.MicroTasks;

[TestFixture]
public sealed class SelectChoicesMicroTaskTests
{
	[Test]
	[SetUICulture("en-US")]
	public async Task Execute_UserSelectsOptions_RecordsEveryOptionAsSelectedOrNot()
	{
		// Arrange
		var context = new InstallStepContext();
		var action = new InstallAction.SelectChoices(Text("Заголовок", "Title"),
		[
			new InstallChoiceStep
			{
				Title = Text("Страница 1", "Page 1"),
				Subtitle = Text("Подзаголовок", "Subtitle"),
				SelectionMode = ChoiceSelectionMode.Any,
				Options =
				[
					new InstallChoiceOption { Id = "A", Name = Text("А", "A"), Description = Text("Описание", "Description"), IsRequired = true },
					new InstallChoiceOption { Id = "B", Name = Text("Б", "B"), IsSelectedByDefault = true },
				],
			},
			new InstallChoiceStep
			{
				Title = Text("Страница 2", "Page 2"),
				SelectionMode = ChoiceSelectionMode.ExactlyOne,
				Options =
				[
					new InstallChoiceOption { Id = "C", Name = Text("В", "C") },
					new InstallChoiceOption { Id = "D", Name = Text("Г", "D") },
				],
			},
		]);
		var sut = CreateSut(context, action);
		using var subject = new Subject<MicroTaskStep>();
		ChoiceSelectionContext? shown = null;
		using var subscription = subject.Subscribe(step =>
		{
			if (step is MicroTaskStep.SelectChoices(var selectionContext))
			{
				shown = selectionContext;
				var (first, second) = (selectionContext.Steps[0], selectionContext.Steps[1]);
				selectionContext.SetResult(
				[
					new ChoiceStepSelection { Step = first, SelectedOptions = [first.Options[0]] },
					new ChoiceStepSelection { Step = second, SelectedOptions = [second.Options[1]] },
				]);
			}
		});

		// Act
		await sut.Execute(subject, CancellationToken.None);

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(shown!.Title.Value, Is.EqualTo(new ChoiceText.Content("Title")));
			var first = shown.Steps[0];
			Assert.That(first.Title.Value, Is.EqualTo(new ChoiceText.Content("Page 1")));
			Assert.That(first.Subtitle.Value, Is.EqualTo(new ChoiceText.Content("Subtitle")));
			Assert.That(first.SelectionMode, Is.EqualTo(ChoiceSelectionMode.Any));
			Assert.That(first.Options.Select(x => x.Name.Value), Is.EqualTo(new[] { new ChoiceText.Content("A"), new ChoiceText.Content("B") }));
			Assert.That(first.Options.Select(x => x.Description.Value),
				Is.EqualTo(new[] { new ChoiceText.Content("Description"), new ChoiceText.Content("") }));
			Assert.That(first.Options.Select(x => (x.IsRequired, x.IsSelectedByDefault)), Is.EqualTo(new[] { (true, false), (false, true) }));
			Assert.That(shown.Steps[1].Subtitle.Value, Is.EqualTo(new ChoiceText.Content("")));
			Assert.That(new[] { "A", "B", "C", "D" }.Select(context.GetValue), Is.EqualTo(new[] { "true", "false", "false", "true" }));
		});
	}

	[Test]
	public void Execute_Cancelled_RecordsNothing()
	{
		// Arrange
		var context = new InstallStepContext();
		var action = new InstallAction.SelectChoices(Text("Заголовок", "Title"),
		[
			new InstallChoiceStep
			{
				Title = Text("Страница", "Page"),
				SelectionMode = ChoiceSelectionMode.Any,
				Options = [new InstallChoiceOption { Id = "A", Name = Text("А", "A") }],
			},
		]);
		var sut = CreateSut(context, action);
		using var subject = new Subject<MicroTaskStep>();
		using var cancellation = new CancellationTokenSource();
		using var subscription = subject.Subscribe(_ => cancellation.Cancel());

		// Act
		// Assert
		Assert.That(async () => await sut.Execute(subject, cancellation.Token), Throws.InstanceOf<OperationCanceledException>());
		Assert.That(context.GetValue("A"), Is.Null);
	}

	private static SelectChoicesMicroTask CreateSut(InstallStepContext context, InstallAction.SelectChoices action)
	{
		var mocker = new AutoMocker(MockBehavior.Strict);
		mocker.Use(context);
		mocker.Use(action);
		mocker.Use<ILogger<SelectChoicesMicroTask>>(NullLogger<SelectChoicesMicroTask>.Instance);
		return mocker.CreateInstance<SelectChoicesMicroTask>(enablePrivate: true);
	}

	private static LocalizedText Text(string russian, string english) => new() { ["ru"] = russian, ["en"] = english };
}
