using FomodInstaller.Scripting.XmlScript;
using LauncherKit.Core.Choices;
using LauncherKit.Core.Fomod;
using LauncherKit.Infrastructure;

namespace LauncherKit.Core.Tests.Fomod;

[TestFixture]
public sealed class FomodChoicesTests
{
	[Test]
	public void Steps_FomodScript_MapsFirstOptionGroupOfEveryInstallStep()
	{
		// Arrange
		var installSteps = LoadInstallSteps();

		// Act
		var sut = new FomodChoices(installSteps);

		// Assert
		var start = sut.Steps[0];
		var weather = sut.Steps[1];
		Assert.Multiple(() =>
		{
			Assert.That(sut.Steps, Has.Count.EqualTo(installSteps.Count));
			Assert.That(start.SelectionMode, Is.EqualTo(ChoiceSelectionMode.Any));
			Assert.That(start.Options.Single().IsRequired, Is.True);
			Assert.That(weather.Title.Value, Is.EqualTo(new ChoiceText.Content("Погодный мод")));
			Assert.That(weather.Subtitle.Value, Is.EqualTo(new ChoiceText.Content("Выберите погодный мод")));
			Assert.That(weather.SelectionMode, Is.EqualTo(ChoiceSelectionMode.ExactlyOne));
			Assert.That(weather.Options[0].Name.Value, Is.EqualTo(new ChoiceText.Content("Azurite CS")));
			Assert.That(weather.Options[0].IsRequired, Is.False);
		});
	}

	[Test]
	public void ToConfiguredInstallSteps_Selection_ReturnsOriginalStepsAndOptions()
	{
		// Arrange
		var installSteps = LoadInstallSteps();
		var sut = new FomodChoices(installSteps);
		var weather = sut.Steps[1];
		ChoiceStepSelection[] selections =
		[
			new() { Step = sut.Steps[0], SelectedOptions = sut.Steps[0].Options },
			new() { Step = weather, SelectedOptions = [weather.Options[1]] },
		];

		// Act
		var configured = sut.ToConfiguredInstallSteps(selections);

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(configured.Select(x => x.Step), Is.EqualTo(new[] { installSteps[0], installSteps[1] }));
			Assert.That(configured[0].SelectedOptions, Is.EqualTo(installSteps[0].OptionGroups[0].Options));
			Assert.That(configured[1].SelectedOptions, Is.EqualTo(new[] { installSteps[1].OptionGroups[0].Options[1] }));
		});
	}

	[Test]
	public void Steps_NotUsableOption_IsUnavailableAndOtherOptionsStayAvailable()
	{
		// Arrange
		var group = new OptionGroup("Group", OptionGroupType.SelectAny, SortOrder.Explicit);
		group.Options.Add(new Option("Unusable", "", "", new StaticOptionTypeResolver(OptionType.NotUsable)));
		group.Options.Add(new Option("Optional", "", "", new StaticOptionTypeResolver(OptionType.Optional)));
		var installStep = new InstallStep("Step", null, SortOrder.Explicit);
		installStep.OptionGroups.Add(group);

		// Act
		var sut = new FomodChoices([installStep]);

		// Assert
		var options = sut.Steps.Single().Options;
		Assert.Multiple(() =>
		{
			Assert.That(options[0].IsAvailable, Is.False);
			Assert.That(options[0].UnavailableReason?.Value, Is.EqualTo(ChoiceText.UnavailableFomodOption.Instance));
			Assert.That(options[1].IsAvailable, Is.True);
		});
	}

	[Test]
	public void ToConfiguredInstallSteps_OptionFromOtherChoices_ThrowsArgumentException()
	{
		// Arrange
		var installSteps = LoadInstallSteps();
		var sut = new FomodChoices(installSteps);
		var foreignStep = new FomodChoices(installSteps).Steps[1];
		ChoiceStepSelection[] selections = [new() { Step = sut.Steps[1], SelectedOptions = [foreignStep.Options[0]] }];

		// Act
		// Assert
		Assert.That(() => sut.ToConfiguredInstallSteps(selections), Throws.ArgumentException);
	}

	private static IReadOnlyList<InstallStep> LoadInstallSteps()
	{
		var xml = File.ReadAllText(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "ModuleConfig.xml"));
		return new XmlScriptType().LoadScript(xml, true).InstallSteps.ToArray();
	}
}
