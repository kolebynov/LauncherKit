using LauncherKit.Infrastructure;
using NuGet.Versioning;
using LauncherKit.Core.AppTasks;
using LauncherKit.Core.AppTasks.ModpackComponents;
using LauncherKit.Core.Choices;
using LauncherKit.Core.Feed;

namespace LauncherKit.Core.Tests.AppTasks.ModpackComponents;

[TestFixture]
public sealed class ModpackComponentChoicesTests
{
	[Test]
	public void Apply_GraphPackAndVoicesChosen_StoresChosenGraphPackTaskAndVoices()
	{
		// Arrange
		var fullTask = CreateAppTask("Full");
		var selection = new ModpackComponentsSelection();
		var sut = new ModpackComponentChoices(
			CreateOffer([new GraphPackComponent("Lite", "", CreateAppTask("Lite"), []), new GraphPackComponent("Full", "", fullTask, [])], CreateEnglishVoices([new DownloadInfo { S3Path = "voices.7z" }]), selection),
			_ => 0);

		// Act
		sut.Apply(
		[
			new ChoiceStepSelection { Step = sut.Steps[0], SelectedOptions = [sut.Steps[0].Options[1]] },
			new ChoiceStepSelection { Step = sut.Steps[1], SelectedOptions = [sut.Steps[1].Options[0]] },
		]);

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(selection.InstallTasks.GetValueOrDefault("GraphPack"), Is.SameAs(fullTask));
			Assert.That((selection.InstallTasks.GetValueOrDefault("EnglishVoices") != null), Is.True);
		});
	}

	[Test]
	public void Apply_NoEnglishVoicesOffered_AsksOnlyGraphPackAndSkipsVoices()
	{
		// Arrange
		var selection = new ModpackComponentsSelection();
		var sut = new ModpackComponentChoices(
			CreateOffer([new GraphPackComponent("Lite", "", CreateAppTask("Lite"), [])], null, selection),
			_ => 0);

		// Act
		sut.Apply([new ChoiceStepSelection { Step = sut.Steps[0], SelectedOptions = [sut.Steps[0].Options[0]] }]);

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(sut.Steps.Select(x => x.Title.Value), Is.EqualTo(new[] { new ChoiceText.Content("Графпак") }));
			Assert.That(selection.IsApplied, Is.True);
			Assert.That((selection.InstallTasks.GetValueOrDefault("EnglishVoices") != null), Is.False);
		});
	}

	[Test]
	public void Apply_NoGraphPacksOffered_AsksOnlyVoicesAndInstallsNoGraphPack()
	{
		// Arrange
		var selection = new ModpackComponentsSelection();
		var sut = new ModpackComponentChoices(CreateOffer([], CreateEnglishVoices([new DownloadInfo { S3Path = "voices.7z" }]), selection), _ => 0);

		// Act
		sut.Apply([new ChoiceStepSelection { Step = sut.Steps[0], SelectedOptions = [sut.Steps[0].Options[0]] }]);

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(sut.Steps.Select(x => x.Title.Value), Is.EqualTo(new[] { new ChoiceText.Content("Английская озвучка") }));
			Assert.That(selection.InstallTasks.GetValueOrDefault("GraphPack"), Is.Null);
			Assert.That((selection.InstallTasks.GetValueOrDefault("EnglishVoices") != null), Is.True);
		});
	}

	[Test]
	public void Apply_DefaultGraphPackUnavailable_ShowsReasonAndDefaultsToNothing()
	{
		// Arrange
		var selection = new ModpackComponentsSelection();
		selection.InstallTasks["GraphPack"] = CreateAppTask("Stale");
		selection.InstallTasks["EnglishVoices"] = CreateAppTask("StaleVoices");
		var sut = new ModpackComponentChoices(
			CreateOffer([new GraphPackComponent("Lite", "", null, [], IsDefault: true), new GraphPackComponent("Full", "", CreateAppTask("Full"), [])], CreateEnglishVoices([new DownloadInfo { S3Path = "voices.7z" }]), selection),
			_ => 0);

		// Act
		sut.Apply(sut.Steps
			.Select(step => new ChoiceStepSelection
			{
				Step = step,
				SelectedOptions = step.Options.Where(x => x.IsSelectedByDefault).ToArray(),
			})
			.ToArray());

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(sut.Steps.Select(x => x.SelectionMode),
				Is.EqualTo(new[] { ChoiceSelectionMode.ExactlyOne, ChoiceSelectionMode.ExactlyOne }));
			Assert.That(sut.Steps[0].Options.Select(x => x.Name.Value), Is.EqualTo(new object[] { new ChoiceText.Content("Lite"), new ChoiceText.Content("Full"), ChoiceText.NoneOption.Instance }));
			Assert.That(sut.Steps[0].Options.Select(x => x.UnavailableReason?.Value),
				Is.EqualTo(new object?[] { ChoiceText.UnavailableGraphPackUpdating.Instance, null, null }));
			Assert.That(sut.Steps[1].Options.Select(x => x.IsAvailable), Is.EqualTo(new[] { true, true }));
			Assert.That(selection.InstallTasks.GetValueOrDefault("GraphPack"), Is.Null);
			Assert.That((selection.InstallTasks.GetValueOrDefault("EnglishVoices") != null), Is.False);
		});
	}

	[Test]
	public void Apply_DefaultGraphPackAvailable_DefaultsToThatGraphPack()
	{
		// Arrange
		var liteTask = CreateAppTask("Lite");
		var selection = new ModpackComponentsSelection();
		var sut = new ModpackComponentChoices(
			CreateOffer([new GraphPackComponent("Lite", "", liteTask, [], IsDefault: true), new GraphPackComponent("Full", "", CreateAppTask("Full"), [])], CreateEnglishVoices([new DownloadInfo { S3Path = "voices.7z" }]), selection),
			_ => 0);

		// Act
		sut.Apply(sut.Steps
			.Select(step => new ChoiceStepSelection
			{
				Step = step,
				SelectedOptions = step.Options.Where(x => x.IsSelectedByDefault).ToArray(),
			})
			.ToArray());

		// Assert
		Assert.That(selection.InstallTasks.GetValueOrDefault("GraphPack"), Is.SameAs(liteTask));
	}

	[Test]
	public void Steps_SeveralDefaultGraphPacks_SelectsOnlyFirstAvailableByDefault()
	{
		// Arrange
		var sut = new ModpackComponentChoices(
			CreateOffer([
					new GraphPackComponent("Lite", "", null, [], IsDefault: true),
					new GraphPackComponent("Medium", "", CreateAppTask("Medium"), [], IsDefault: true),
					new GraphPackComponent("Full", "", CreateAppTask("Full"), [], IsDefault: true),
				], CreateEnglishVoices([new DownloadInfo { S3Path = "voices.7z" }]), new ModpackComponentsSelection()),
			_ => 0);

		// Act
		var result = sut.Steps[0].Options.Select(x => x.IsSelectedByDefault);

		// Assert
		Assert.That(result, Is.EqualTo(new[] { false, true, false, false }));
	}

	[Test]
	public void Steps_DownloadingOptions_DescribeTotalDownloadSize()
	{
		// Arrange
		const long gigaByte = 1024L * 1024 * 1024;
		var part1 = new DownloadInfo { S3Path = "full1.7z" };
		var part2 = new DownloadInfo { S3Path = "full2.7z" };
		var voices = new DownloadInfo { S3Path = "voices.7z" };
		var voicesExtra = new DownloadInfo { S3Path = "voices-extra.7z" };
		var sizes = new Dictionary<DownloadInfo, long>
		{
			[part1] = 10 * gigaByte, [part2] = gigaByte / 2, [voices] = gigaByte / 2, [voicesExtra] = gigaByte / 4,
		};

		// Act
		var sut = new ModpackComponentChoices(
			CreateOffer([new GraphPackComponent("Lite", "Слабая видеокарта", null, []), new GraphPackComponent("Full", "Мощная видеокарта", CreateAppTask("Full"), [part1, part2])], CreateEnglishVoices([voices, voicesExtra]), new ModpackComponentsSelection()),
			x => sizes[x]);

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(sut.Steps[0].Options.Select(x => x.Description.Value), Is.EqualTo(new object[]
			{
				new ChoiceText.ComponentOptionDescription("Lite", "Слабая видеокарта", null, false),
				new ChoiceText.ComponentOptionDescription("Full", "Мощная видеокарта", 10 * gigaByte + gigaByte / 2, false),
				new ChoiceText.Content(""),
			}));
			Assert.That(sut.Steps[1].Options.Select(x => x.Description.Value), Is.EqualTo(new object[]
			{
				new ChoiceText.ComponentOptionDescription("Да", "Английская озвучка", 3 * gigaByte / 4, true),
				new ChoiceText.Content(""),
			}));
		});
	}

	[Test]
	[SetUICulture("en-US")]
	public void Steps_DeclaredModComponent_UsesLocalizedFeedPromptAndStoresTaskUnderDeclaredId()
	{
		var task = CreateAppTask("Install_CustomMod");
		var selection = new ModpackComponentsSelection();
		var definition = new InstallComponent
		{
			Id = "CustomTextures",
			Kind = InstallComponentKind.Mod,
			ModName = "Custom Textures",
			Title = new LocalizedText { ["ru"] = "Текстуры", ["en"] = "Textures" },
			Description = new LocalizedText { ["ru"] = "Установить текстуры?", ["en"] = "Install textures?" },
			SupportedModpackVersion = VersionRange.All,
		};
		var sut = new ModpackComponentChoices(new ModpackComponentsOffer(
			[new ModpackComponentOffer(definition, [new GraphPackComponent("Да", "Textures", task, [])])], selection), _ => 0);

		sut.Apply([new ChoiceStepSelection { Step = sut.Steps.Single(), SelectedOptions = [sut.Steps.Single().Options[0]] }]);

		Assert.Multiple(() =>
		{
			Assert.That(sut.Steps.Single().Title.Value, Is.EqualTo(new ChoiceText.Content("Textures")));
			Assert.That(sut.Steps.Single().Subtitle.Value, Is.EqualTo(new ChoiceText.Content("Install textures?")));
			Assert.That(selection.InstallTasks["CustomTextures"], Is.SameAs(task));
			Assert.That(selection.IsApplied, Is.True);
		});
	}

	private static AppTask CreateAppTask(string id) => new(id, new AppTaskDescription.InstallMod(id), _ => [], [], null);

	private static GraphPackComponent CreateEnglishVoices(IReadOnlyList<DownloadInfo> downloads) =>
		new("Да", "Английская озвучка", CreateAppTask("Install_Voices"), downloads);

	private static ModpackComponentsOffer CreateOffer(IReadOnlyList<GraphPackComponent> graphPacks,
		GraphPackComponent? voices, ModpackComponentsSelection selection)
	{
		var components = new List<ModpackComponentOffer>();
		if (graphPacks.Count > 0)
		{
			components.Add(new ModpackComponentOffer(Definition("GraphPack", InstallComponentKind.GraphPack, "Графпак", 1), graphPacks));
		}
		if (voices != null)
		{
			components.Add(new ModpackComponentOffer(Definition("EnglishVoices", InstallComponentKind.Mod, "Английская озвучка", 0), [voices]));
		}
		return new ModpackComponentsOffer(components, selection);
	}

	private static InstallComponent Definition(string id, InstallComponentKind kind, string title, int order) => new()
	{
		Id = id,
		Kind = kind,
		ModName = kind == InstallComponentKind.Mod ? "Voices" : null,
		Title = new LocalizedText { ["ru"] = title },
		Description = new LocalizedText { ["ru"] = $"Описание: {title}" },
		SupportedModpackVersion = VersionRange.All,
		Order = order,
	};
}
