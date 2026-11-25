namespace LauncherKit.Core.Feed;

/// <summary>A feed download from the server, published by <see cref="IFeedProvider.LoadEvents"/>.</summary>
public union FeedLoadEvent(FeedLoadEvent.Started, FeedLoadEvent.Succeeded, FeedLoadEvent.Failed)
{
	public sealed record Started;

	public sealed record Succeeded;

	/// <summary>
	/// The feed could not be loaded; until it retries, the provider returns the last loaded feed, or the empty default
	/// feed when none has loaded yet.
	/// </summary>
	public sealed record Failed(Exception Exception);
}
