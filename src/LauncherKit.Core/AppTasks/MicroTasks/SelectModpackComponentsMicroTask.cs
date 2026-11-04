using LauncherKit.Infrastructure;
using R3;
using LauncherKit.Core.AppTasks.ModpackComponents;
using LauncherKit.Core.Choices;
using LauncherKit.Core.FileSources;
using LauncherKit.Core.Services.Abstractions;

namespace LauncherKit.Core.AppTasks.MicroTasks;

/// <summary>
/// Looks up the sizes of the offered components' downloads, asks the user the <see cref="ModpackComponentChoices"/>, and
/// applies the answers. The sizes are requested only here, when the choices are about to be shown, so building the
/// installation chain stays free of network requests.
/// </summary>
internal sealed class SelectModpackComponentsMicroTask : IAppMicroTask
{
	private static readonly MicroTaskStep GetDownloadSizesStep = new MicroTaskStep.MicroOperation(MicroTaskDescription.GetDownloadSizes.Instance);

	private readonly ModpackComponentsOffer _offer;
	private readonly IDownloadServerFallback _downloadServerFallback;
	private readonly IFeedFileSourceFactory _feedFileSourceFactory;

	public MicroTaskDescription Description { get; } = MicroTaskDescription.SelectModpackComponents.Instance;

	public SelectModpackComponentsMicroTask(ModpackComponentsOffer offer, IDownloadServerFallback downloadServerFallback,
		IFeedFileSourceFactory feedFileSourceFactory)
	{
		_offer = offer;
		_downloadServerFallback = downloadServerFallback;
		_feedFileSourceFactory = feedFileSourceFactory;
	}

	public async ValueTask Execute(ISubject<MicroTaskStep> stepSubject, CancellationToken cancellationToken)
	{
		stepSubject.OnNext(GetDownloadSizesStep);
		var downloads = _offer.Components.SelectMany(x => x.Options).SelectMany(x => x.Downloads)
			.Distinct().ToList();
		var sizes = await _downloadServerFallback.Execute(
			UrlKeys.Feed,
			(feedBaseUri, ct) => Task.WhenAll(downloads.Select(x =>
				_feedFileSourceFactory.Create(x, feedBaseUri).GetFileSize(ct).AsTask())),
			cancellationToken);
		var sizeByDownload = downloads.Zip(sizes).ToDictionary(x => x.First, x => x.Second);
		var componentChoices = new ModpackComponentChoices(_offer, x => sizeByDownload[x]);

		using var choiceSelectionContext = new ChoiceSelectionContext(ModpackComponentChoices.Title,
			componentChoices.Steps, cancellationToken);
		stepSubject.OnNext(new MicroTaskStep.SelectChoices(choiceSelectionContext));
		componentChoices.Apply(await choiceSelectionContext.ResultTask);
	}
}
