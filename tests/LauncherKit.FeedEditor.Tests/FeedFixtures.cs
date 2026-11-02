namespace LauncherKit.FeedEditor.Tests;

internal static class FeedFixtures
{
	/// <summary>The V12 contract fixture edited by the current editor.</summary>
	public static string StoredFeedJson() =>
		File.ReadAllText(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "feedV12.json"));

	public static string LegacyFeedJson() =>
		File.ReadAllText(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "feed.json"));
}
