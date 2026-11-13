using NuGet.Versioning;
using R3;
using LauncherKit.Core.AppTasks;
using LauncherKit.Core.AppTasks.MicroTasks;
using LauncherKit.Core.AppTasks.ModpackComponents;
using LauncherKit.Core.Choices;
using LauncherKit.Core.Feed;
using LauncherKit.Core.FileSources;
using LauncherKit.Core.Models;
using LauncherKit.Core.Services;
using LauncherKit.Core.Tests.TestSupport;
using LauncherKit.Core.Utils;

namespace LauncherKit.Core.Tests.AppTasks.MicroTasks;

[TestFixture]
public sealed class SelectModpackComponentsMicroTaskTests
{
	[Test]
	public async Task Execute_ComponentsOffered_ShowsFetchedDownloadSizesAndAppliesAnswers()
	{
		// Arrange
		var mocker = new AutoMocker(MockBehavior.Strict);
		var part1 = new DownloadInfo { S3Path = "full1.7z" };
		var part2 = new DownloadInfo { S3Path = "full2.7z" };
		var voices = new DownloadInfo { S3Path = "voices.7z" };
		var feedBaseUri = new Uri("https://proxy.test/");
		DownloadServerFallbackStubs.SingleServer<long[]>(mocker, feedBaseUri);
		var feedFileSourceFactory = mocker.GetMock<IFeedFileSourceFactory>();
		feedFileSourceFactory.Setup(x => x.Create(part1, feedBaseUri)).Returns(new SizedFileSource(BytesFormatter.GigaByte));
		feedFileSourceFactory.Setup(x => x.Create(part2, feedBaseUri)).Returns(new SizedFileSource(BytesFormatter.GigaByte));
		feedFileSourceFactory.Setup(x => x.Create(voices, feedBaseUri)).Returns(new SizedFileSource(BytesFormatter.GigaByte / 2));
		var fullTask = new AppTask("Full", new AppTaskDescription.Custom("Full"), _ => [], [], null);
		var selection = new ModpackComponentsSelection();
		var voicesTask = new AppTask("Install_Voices", new AppTaskDescription.Custom("Voices"), _ => [], [], null);
		mocker.Use(new ModpackComponentsOffer(
		[
			new ModpackComponentOffer(Definition("EnglishVoices", InstallComponentKind.Mod, "Английская озвучка", 0),
				[new ModComponentOption("Английская озвучка", null, voicesTask, [voices])]),
			new ModpackComponentOffer(Definition("GraphPack", InstallComponentKind.ModChoice, "Графпак", 1),
				[new ModComponentOption("Full", "Мощная видеокарта", fullTask, [part1, part2])]),
		], selection));
		var sut = mocker.CreateInstance<SelectModpackComponentsMicroTask>(enablePrivate: true);
		using var subject = new Subject<MicroTaskStep>();
		IReadOnlyList<ChoiceStep>? shownSteps = null;
		using var subscription = subject.Subscribe(step =>
		{
			if (step is MicroTaskStep.SelectChoices(var selectionContext))
			{
				shownSteps = selectionContext.Steps;
				selectionContext.SetResult(shownSteps
					.Select(x => new ChoiceStepSelection { Step = x, SelectedOptions = [x.Options[0]] })
					.ToArray());
			}
		});

		// Act
		await sut.Execute(subject, CancellationToken.None);

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(shownSteps!.Select(x => x.Title.Value), Is.EqualTo(new[] { new ChoiceText.Content("Английская озвучка"), new ChoiceText.Content("Графпак") }));
			Assert.That(shownSteps![0].Options[0].Description.Value,
				Is.EqualTo(new ChoiceText.ComponentOptionDescription("Английская озвучка", null, BytesFormatter.GigaByte / 2)));
			Assert.That(shownSteps![1].Options[0].Description.Value,
				Is.EqualTo(new ChoiceText.ComponentOptionDescription("Full", "Мощная видеокарта", 2L * BytesFormatter.GigaByte)));
			Assert.That(selection.InstallTasks["GraphPack"], Is.SameAs(fullTask));
			Assert.That(selection.InstallTasks["EnglishVoices"], Is.SameAs(voicesTask));
			Assert.That(selection.IsApplied, Is.True);
		});
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

	private sealed class SizedFileSource(long size) : IFileSource
	{
		public ValueTask<long> GetFileSize(CancellationToken cancellationToken) => ValueTask.FromResult(size);

		public ValueTask<bool> IsOffsetSupported(CancellationToken cancellationToken) =>
			throw new NotSupportedException();

		public ValueTask<Sha256Hash?> GetHash(CancellationToken cancellationToken) =>
			throw new NotSupportedException();

		public ValueTask<IFileStream> OpenStream(long? fromInclusive, long? toInclusive,
			CancellationToken cancellationToken) =>
			throw new NotSupportedException();
	}
}
