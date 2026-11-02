using System.Text.Json.Nodes;
using LauncherKit.FeedEditor.Feed;
using LauncherKit.FeedEditor.Feed.Json;

namespace LauncherKit.FeedEditor.Tests.Feed.Json;

[TestFixture]
public sealed class LegacyFeedJsonTests
{
	[Test]
	public void SerializeLegacy_EditingLaunchers_PreservesEveryOtherFrozenV11Value()
	{
		var original = JsonNode.Parse(FeedFixtures.LegacyFeedJson())!.AsObject();
		var legacy = FeedJson.DeserializeLegacy(original.ToJsonString());
		legacy.Launchers[0].Version = "9.9.9";
		legacy.Launchers[0].DownloadInfo.S3Path = "Launcher/STBLauncher_9.9.9.exe";
		legacy.Launchers[0].ChangelogPath = "Changelogs/Launcher/9.9.9.md";
		legacy.Launchers[0].SupportsNewStbVersions = true;
		legacy.Launchers.Add(new LegacyLauncherModel
		{
			Version = "10.0.0",
			DownloadInfo = new DownloadInfoModel { S3Path = "Launcher/STBLauncher_10.0.0.exe" },
		});

		var result = JsonNode.Parse(FeedJson.SerializeLegacy(legacy))!.AsObject();

		Assert.Multiple(() =>
		{
			Assert.That(result["Launchers"]![0]!["Version"]!.GetValue<string>(), Is.EqualTo("9.9.9"));
			Assert.That(result["Launchers"]![0]!["ChangelogPath"]!.GetValue<string>(), Is.EqualTo("Changelogs/Launcher/9.9.9.md"));
			Assert.That(result["Launchers"]![0]!["SupportsNewStbVersions"]!.GetValue<bool>(), Is.True);
			Assert.That(result["Launchers"]!.AsArray(), Has.Count.EqualTo(legacy.Launchers.Count));
			Assert.That(result.ContainsKey("Modpacks"), Is.False);
			Assert.That(result.ContainsKey("DefaultLanguage"), Is.False);
		});
		original.Remove("Launchers");
		result.Remove("Launchers");
		Assert.That(JsonNode.DeepEquals(result, original), Is.True, "The legacy editor may only change Launchers.");
	}

	[Test]
	public void SerializeLegacy_UnknownRootLauncherAndDownloadProperties_SurviveWithoutParsingV11Content()
	{
		var original = JsonNode.Parse(FeedFixtures.LegacyFeedJson())!.AsObject();
		original["FutureRoot"] = JsonNode.Parse("""{"enabled":true,"items":[1,null,"x"]}""");
		original["Stb"]![0]!["FutureInstallAction"] = JsonNode.Parse("""{"$type":"UnknownV11Action","value":17}""");
		original["Launchers"]![0]!["FutureLauncher"] = "untouched";
		original["Launchers"]![0]!["DownloadInfo"]!["FutureSize"] = 123;

		var result = JsonNode.Parse(FeedJson.SerializeLegacy(FeedJson.DeserializeLegacy(original.ToJsonString())))!;

		Assert.Multiple(() =>
		{
			Assert.That(JsonNode.DeepEquals(result["FutureRoot"], original["FutureRoot"]), Is.True);
			Assert.That(JsonNode.DeepEquals(result["Stb"], original["Stb"]), Is.True);
			Assert.That(result["Launchers"]![0]!["FutureLauncher"]!.GetValue<string>(), Is.EqualTo("untouched"));
			Assert.That(result["Launchers"]![0]!["DownloadInfo"]!["FutureSize"]!.GetValue<int>(), Is.EqualTo(123));
		});
	}

	[Test]
	public void ValidateLegacy_InvalidOpaqueModpackContent_OnlyValidatesLaunchers()
	{
		var json = JsonNode.Parse(FeedFixtures.LegacyFeedJson())!.AsObject();
		json["Stb"] = "not a current modpack model";
		json["SkyrimDownloadInfo"] = null;
		var legacy = FeedJson.DeserializeLegacy(json.ToJsonString());

		Assert.That(FeedValidator.ValidateLegacy(legacy), Is.Empty);
		legacy.Launchers[0].Version = "next";
		Assert.That(FeedValidator.ValidateLegacy(legacy).Select(error => error.Location),
			Is.EqualTo(new[] { "Лаунчер next › Версия" }));
	}
}
