namespace LauncherKit.FeedEditor.Tests;

internal static class FeedFixtures
{
	/// <summary>The feed contract fixture (V11) edited by the editor.</summary>
	public static string StoredFeedJson() =>
		File.ReadAllText(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "feedV11.json"));
}
