using System.Text.Json;
using System.Text.Json.Nodes;

namespace LauncherKit.Core.Tests;

[TestFixture]
public sealed class SourceGeneratedJsonContextTests
{
	[Test]
	public void FeedData_ConvertedRealFeed_ReadsEveryCaseAsItsType()
	{
		// Arrange
		var feedJson = ReadFixture("feedV11.json");
		var mods = JsonNode.Parse(feedJson)!["Mods"]!.AsArray();

		// Act
		var feed = JsonSerializer.Deserialize(feedJson, SourceGeneratedJsonContext.Default.FeedData)!;

		// Assert
		Assert.That(
			feed.Mods!.SelectMany(mod => mod.Items).SelectMany(item => item.InstallSteps ?? []).SelectMany(step => step.Actions)
				.Select(action => action.GetType().Name),
			Is.EqualTo(Actions(mods).Select(TypeOf)).And.Not.Empty);
	}

	[Test]
	public void FeedData_ConvertedRealFeed_WritesSameCases()
	{
		// Arrange
		var feedJson = ReadFixture("feedV11.json");
		var feed = JsonSerializer.Deserialize(feedJson, SourceGeneratedJsonContext.Default.FeedData);

		// Act
		var writtenJson = JsonSerializer.Serialize(feed, SourceGeneratedJsonContext.Default.FeedData);

		// Assert
		var expectedMods = JsonNode.Parse(feedJson)!["Mods"]!.AsArray();
		var writtenMods = JsonNode.Parse(writtenJson)!["Mods"]!.AsArray();
		Assert.That(
			Actions(writtenMods).Select(action => WithoutWriteOnlyValues(action).ToJsonString()),
			Is.EqualTo(Actions(expectedMods).Select(action => action.ToJsonString())));
	}

	private static IEnumerable<JsonNode> Actions(JsonArray mods) =>
		mods.SelectMany(mod => mod!["Items"]!.AsArray())
			.SelectMany(item => item!["InstallSteps"]?.AsArray() ?? [])
			.SelectMany(step => step!["Actions"]!.AsArray())
			.Select(action => action!);

	private static string TypeOf(JsonNode? node) => node!["$type"]!.GetValue<string>();

	/// <summary>Drops what only the launcher writes: the computed <c>DownloadInfo.FileName</c> and a null hash.</summary>
	private static JsonNode WithoutWriteOnlyValues(JsonNode action)
	{
		if (action["DownloadInfo"] is JsonObject downloadInfo)
		{
			downloadInfo.Remove("FileName");
			if (downloadInfo["Hash"] is null)
			{
				downloadInfo.Remove("Hash");
			}
		}

		return action;
	}

	private static string ReadFixture(string fileName) =>
		File.ReadAllText(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "Feed", fileName));
}
