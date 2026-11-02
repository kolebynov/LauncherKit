using Microsoft.Extensions.Logging.Abstractions;
using R3;
using LauncherKit.Core.AppTasks.MicroTasks;
using LauncherKit.Core.Choices;
using LauncherKit.Core.Models;
using LauncherKit.Core.Services.Abstractions;

namespace LauncherKit.Core.Tests.AppTasks.MicroTasks;

[TestFixture]
public sealed class SelectRequiredModMicroTaskTests
{
	[Test]
	public async Task Execute_RequiredModAlreadyActive_SelectsItWithoutPrompting()
	{
		// Arrange
		var context = new InstallStepContext();
		var mocker = new AutoMocker(MockBehavior.Strict);
		mocker.Use(context);
		mocker.Use<Microsoft.Extensions.Logging.ILogger<SelectRequiredModMicroTask>>(
			NullLogger<SelectRequiredModMicroTask>.Instance);
		mocker.Use<IReadOnlyList<string>>(["Required Mod"]);
		mocker.GetMock<IModOrganizerService>()
			.Setup(x => x.GetModList(CancellationToken.None))
			.ReturnsAsync([new ModListItem("Required Mod", true), new ModListItem("Other", false)]);
		var sut = mocker.CreateInstance<SelectRequiredModMicroTask>(enablePrivate: true);
		using var subject = new Subject<MicroTaskStep>();

		// Act
		await sut.Execute(subject, CancellationToken.None);

		// Assert
		Assert.That(context.GetValue("SelectedMod"), Is.EqualTo("Required Mod"));
		mocker.GetMock<IModOrganizerService>()
			.Verify(x => x.GetModList(CancellationToken.None), Times.Once);
	}

	[Test]
	public async Task Execute_NoRequiredModActive_StoresModChosenByUser()
	{
		// Arrange
		var context = new InstallStepContext();
		var mocker = CreateMocker(context);
		var modOrganizer = mocker.GetMock<IModOrganizerService>();
		modOrganizer.Setup(x => x.GetModList(CancellationToken.None)).ReturnsAsync([new ModListItem("First", false)]);
		modOrganizer.SetupGet(x => x.InstalledMods).Returns([CreateMod("Unrelated"), CreateMod("First"), CreateMod("Second")]);
		var sut = mocker.CreateInstance<SelectRequiredModMicroTask>(enablePrivate: true);
		using var subject = new Subject<MicroTaskStep>();
		ChoiceStep? shownStep = null;
		using var subscription = subject.Subscribe(step =>
		{
			if (step is MicroTaskStep.SelectChoices(var selectionContext))
			{
				shownStep = selectionContext.Steps.Single();
				selectionContext.SetResult([new ChoiceStepSelection { Step = shownStep, SelectedOptions = [shownStep.Options[1]] }]);
			}
		});

		// Act
		await sut.Execute(subject, CancellationToken.None);

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(shownStep!.SelectionMode, Is.EqualTo(ChoiceSelectionMode.ExactlyOne));
			Assert.That(shownStep.Options.Select(x => x.Name.Value), Is.EqualTo(new[] { new ChoiceText.Content("First"), new ChoiceText.Content("Second") }));
			Assert.That(shownStep.Options.Select(x => x.IsSelectedByDefault), Is.EqualTo(new[] { true, false }));
			Assert.That(context.GetValue("SelectedMod"), Is.EqualTo("Second"));
		});
	}

	[Test]
	public void Execute_NoRequiredModInstalled_ThrowsWithoutPrompting()
	{
		// Arrange
		var context = new InstallStepContext();
		var mocker = CreateMocker(context);
		var modOrganizer = mocker.GetMock<IModOrganizerService>();
		modOrganizer.Setup(x => x.GetModList(CancellationToken.None)).ReturnsAsync([]);
		modOrganizer.SetupGet(x => x.InstalledMods).Returns([CreateMod("Unrelated")]);
		var sut = mocker.CreateInstance<SelectRequiredModMicroTask>(enablePrivate: true);
		using var subject = new Subject<MicroTaskStep>();
		var prompted = false;
		using var subscription = subject.Subscribe(_ => prompted = true);

		// Act
		// Assert
		Assert.That(async () => await sut.Execute(subject, CancellationToken.None), Throws.InvalidOperationException);
		Assert.That(prompted, Is.False);
	}

	private static AutoMocker CreateMocker(InstallStepContext context)
	{
		var mocker = new AutoMocker(MockBehavior.Strict);
		mocker.Use(context);
		mocker.Use<Microsoft.Extensions.Logging.ILogger<SelectRequiredModMicroTask>>(
			NullLogger<SelectRequiredModMicroTask>.Instance);
		mocker.Use<IReadOnlyList<string>>(["First", "Second"]);
		return mocker;
	}

	private static ModInfo CreateMod(string name) => new(name, new Lazy<MetaIniData?>(() => null));
}
