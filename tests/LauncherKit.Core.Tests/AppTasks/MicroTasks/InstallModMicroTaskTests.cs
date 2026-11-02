using FomodInstaller.Scripting.XmlScript;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using R3;
using LauncherKit.Core.AppTasks.MicroTasks;
using LauncherKit.Core.Choices;
using LauncherKit.Core.Feed.Mods;
using LauncherKit.Core.FileStorage;
using LauncherKit.Core.Fomod;
using LauncherKit.Core.Services.Abstractions;

namespace LauncherKit.Core.Tests.AppTasks.MicroTasks;

[TestFixture]
public sealed class InstallModMicroTaskTests
{
	private const string ModName = "Example";
	private const string ArchiveName = "example.7z";
	private const string ArchivePath = @"C:\Downloads\example.7z";
	private static readonly ModPlacement Placement = new ModPlacement.UnderSeparator("Extras");

	[Test]
	public async Task Execute_ArchiveWithoutFomod_InstallsWholeArchiveAtPlacementThroughModOrganizer()
	{
		// Arrange
		var mocker = CreateMocker();
		mocker.GetMock<IFileStorageService>().Setup(x => x.GetFilePath(ArchiveName)).Returns(ArchivePath);
		mocker.GetMock<IFomodInstallService>()
			.Setup(x => x.IsFomodArchive(ArchivePath, CancellationToken.None))
			.ReturnsAsync(false);
		mocker.GetMock<IModOrganizerService>()
			.Setup(x => x.InstallMod(ModName, ArchivePath, Placement, CancellationToken.None))
			.Returns(Task.CompletedTask);
		var sut = CreateSut(mocker, Placement);
		using var subject = new Subject<MicroTaskStep>();

		// Act
		await sut.Execute(subject, CancellationToken.None);

		// Assert
		mocker.GetMock<IModOrganizerService>().VerifyAll();
		mocker.GetMock<IFomodInstallService>().Verify(
			x => x.CreateInstaller(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
	}

	[Test]
	public async Task Execute_FomodArchiveWithPlacement_InstallsUserSelectionThenPlacesModAndActivatesIt()
	{
		// Arrange
		var xml = await File.ReadAllTextAsync(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "ModuleConfig.xml"));
		var installSteps = new XmlScriptType().LoadScript(xml, true).InstallSteps;
		var selectedStep = installSteps[1];
		var selectedOption = selectedStep.OptionGroups[0].Options[1];
		var mocker = CreateMocker();
		mocker.GetMock<IFileStorageService>().Setup(x => x.GetFilePath(ArchiveName)).Returns(ArchivePath);
		var installer = mocker.GetMock<IFomodInstaller>();
		installer.SetupGet(x => x.Title).Returns("Example FOMOD");
		installer.SetupGet(x => x.InstallSteps).Returns(installSteps);
		installer.Setup(x => x.Install(
				It.Is<IReadOnlyList<ConfiguredInstallStep>>(steps => steps.Count == installSteps.Count
					&& steps[1].Step == selectedStep
					&& steps[1].SelectedOptions.SequenceEqual(new[] { selectedOption })),
				CancellationToken.None))
			.Returns(Task.CompletedTask);
		var fomodInstallService = mocker.GetMock<IFomodInstallService>();
		fomodInstallService.Setup(x => x.IsFomodArchive(ArchivePath, CancellationToken.None)).ReturnsAsync(true);
		fomodInstallService.Setup(x => x.CreateInstaller(ArchivePath, ModName, CancellationToken.None))
			.ReturnsAsync(installer.Object);
		var calls = new List<string>();
		var modOrganizer = mocker.GetMock<IModOrganizerService>();
		modOrganizer.Setup(x => x.PlaceMod(ModName, Placement, CancellationToken.None))
			.Callback(() => calls.Add("move"))
			.Returns(Task.CompletedTask);
		modOrganizer.Setup(x => x.ActivateMod(ModName, true, true, CancellationToken.None))
			.Callback(() => calls.Add("activate"))
			.Returns(Task.CompletedTask);
		var sut = CreateSut(mocker, Placement);
		using var subject = new Subject<MicroTaskStep>();
		object? selectionTitle = null;
		using var subscription = subject.Subscribe(step =>
		{
			if (step is MicroTaskStep.SelectChoices(var context))
			{
				selectionTitle = context.Title.Value;
				context.SetResult(context.Steps
					.Select((choiceStep, index) => new ChoiceStepSelection
					{
						Step = choiceStep,
						SelectedOptions = index == 1 ? [choiceStep.Options[1]] : [],
					})
					.ToList());
			}
		});

		// Act
		await sut.Execute(subject, CancellationToken.None);

		// Assert
		Assert.That(selectionTitle, Is.EqualTo(new ChoiceText.Content("Example FOMOD")));
		installer.VerifyAll();
		Assert.That(calls, Is.EqualTo(new[] { "move", "activate" }));
		modOrganizer.Verify(
			x => x.InstallMod(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<ModPlacement?>(), It.IsAny<CancellationToken>()),
			Times.Never);
	}

	[Test]
	public void Execute_ArchiveNotDownloaded_ThrowsBeforeInstallation()
	{
		// Arrange
		var mocker = CreateMocker();
		mocker.GetMock<IFileStorageService>().Setup(x => x.GetFilePath(ArchiveName)).Returns((string?)null);
		var sut = CreateSut(mocker, null);
		using var subject = new Subject<MicroTaskStep>();

		// Act
		// Assert
		Assert.That(async () => await sut.Execute(subject, CancellationToken.None),
			Throws.InvalidOperationException);
		mocker.GetMock<IFomodInstallService>().VerifyNoOtherCalls();
		mocker.GetMock<IModOrganizerService>().VerifyNoOtherCalls();
	}

	private static AutoMocker CreateMocker()
	{
		var mocker = new AutoMocker(MockBehavior.Strict);
		mocker.Use<ILogger<InstallModMicroTask>>(NullLogger<InstallModMicroTask>.Instance);
		return mocker;
	}

	private static InstallModMicroTask CreateSut(AutoMocker mocker, ModPlacement? placement) =>
		new(mocker.Get<IFileStorageService>(), mocker.Get<IFomodInstallService>(), mocker.Get<ILogger<InstallModMicroTask>>(),
			mocker.Get<IModOrganizerService>(), ModName, ArchiveName, placement);
}
