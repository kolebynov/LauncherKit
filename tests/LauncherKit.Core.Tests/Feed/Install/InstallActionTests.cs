using System.Text.Json;
using System.Text.Json.Nodes;
using LauncherKit.Core.Choices;
using LauncherKit.Core.Feed;
using LauncherKit.Core.Feed.Install;
using LauncherKit.Core.Feed.Mods;
using LauncherKit.Core.Models;

namespace LauncherKit.Core.Tests.Feed.Install;

[TestFixture]
public sealed class InstallActionTests
{
	private const string Hash = "9DBD20E17BD455D5377BE57692578FDDF9D6D64109AC7280A9A9636CA7E09444";

	private static IEnumerable<TestCaseData> Cases()
	{
		yield return Case(
			new InstallAction.InstallMod(new DownloadInfo { S3Path = "Mods/b.zip", Hash = new Sha256Hash(Hash) }, "B", new ModPlacement.NextToMod("A", ModListSide.Below)),
			$$$"""{"$type":"InstallMod","DownloadInfo":{"S3Path":"Mods/b.zip","Hash":"{{{Hash}}}","FileName":"b.zip"},"ModName":"B","Placement":{"$type":"NextToMod","ModName":"A","Side":"Below"}}""");
		yield return Case(new InstallAction.SelectRequiredMod(["A", "B"]), """{"$type":"SelectRequiredMod","Mods":["A","B"]}""");
		yield return Case(
			new InstallAction.SelectChoices(new LocalizedText { ["ru"] = "Title ru", ["en"] = "Title" },
			[
				new InstallChoiceStep
				{
					Title = new LocalizedText { ["ru"] = "Page" },
					Subtitle = new LocalizedText { ["ru"] = "Subtitle" },
					SelectionMode = ChoiceSelectionMode.AtLeastOne,
					Options =
					[
						new InstallChoiceOption { Id = "A", Name = new LocalizedText { ["ru"] = "Option A" }, IsRequired = true },
						new InstallChoiceOption
						{
							Id = "B", Name = new LocalizedText { ["ru"] = "Option B" }, Description = new LocalizedText { ["ru"] = "About B" },
							IsSelectedByDefault = true,
						},
					],
				},
			]),
			"""{"$type":"SelectChoices","Title":{"ru":"Title ru","en":"Title"},"Steps":[{"Title":{"ru":"Page"},"Subtitle":{"ru":"Subtitle"},"SelectionMode":"AtLeastOne","Options":[{"Id":"A","Name":{"ru":"Option A"},"Description":null,"IsRequired":true,"IsSelectedByDefault":false},{"Id":"B","Name":{"ru":"Option B"},"Description":{"ru":"About B"},"IsRequired":false,"IsSelectedByDefault":true}]}]}""");
		yield return Case(new InstallAction.ActivateMods(true, ["A"]), """{"$type":"ActivateMods","Activate":true,"Mods":["A"]}""");
		yield return Case(
			new InstallAction.ActivatePlugins(false, ["a.esp"]),
			"""{"$type":"ActivatePlugins","Activate":false,"Plugins":["a.esp"]}""");
		yield return Case(
			new InstallAction.RemoveFileSystemItem(["mods/a", "mods/b"], false),
			"""{"$type":"RemoveFileSystemItem","Paths":["mods/a","mods/b"],"FailOnFailedDelete":false}""");
		yield return Case(
			new InstallAction.ExtractArchive(new DownloadInfo { S3Path = "Mods/a.7z", Hash = new Sha256Hash(Hash) }, "[Modpack] Mod Organizer"),
			$$$"""{"$type":"ExtractArchive","DownloadInfo":{"S3Path":"Mods/a.7z","Hash":"{{{Hash}}}","FileName":"a.7z"},"DestinationPath":"[Modpack] Mod Organizer"}""");
		yield return Case(
			new InstallAction.MergePlugins(new DownloadInfo { S3Path = "Mods/plugins.txt", Hash = new Sha256Hash(Hash) }),
			$$$"""{"$type":"MergePlugins","DownloadInfo":{"S3Path":"Mods/plugins.txt","Hash":"{{{Hash}}}","FileName":"plugins.txt"}}""");
		yield return Case(new InstallAction.ChangeProfile("Graphics"), """{"$type":"ChangeProfile","ProfileName":"Graphics"}""");
	}

	[TestCaseSource(nameof(Cases))]
	public void Serialize_Case_WritesTypeFirstAndCaseProperties(InstallAction action, string expectedJson)
	{
		// Act
		var json = JsonSerializer.Serialize(action, SourceGeneratedJsonContext.Default.InstallAction);

		// Assert
		Assert.That(JsonNode.Parse(json)!.ToJsonString(), Is.EqualTo(expectedJson));
	}

	[TestCaseSource(nameof(Cases))]
	public void Deserialize_CaseJson_ReadsSameCase(InstallAction action, string json)
	{
		// Act
		var result = JsonSerializer.Deserialize(json, SourceGeneratedJsonContext.Default.InstallAction);

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(result, Is.TypeOf(action.GetType()));
			Assert.That(
				JsonSerializer.Serialize(result, SourceGeneratedJsonContext.Default.InstallAction),
				Is.EqualTo(JsonSerializer.Serialize(action, SourceGeneratedJsonContext.Default.InstallAction)));
		});
	}

	[Test]
	public void Deserialize_PropertiesInOtherOrder_ReadsPropertyValues()
	{
		// Arrange
		const string json = """{"$type":"ActivatePlugins","Plugins":["a.esp"],"Activate":true}""";

		// Act
		var result = JsonSerializer.Deserialize(json, SourceGeneratedJsonContext.Default.InstallAction);

		// Assert
		Assert.That(result, Is.TypeOf<InstallAction.ActivatePlugins>());
		var activatePlugins = (InstallAction.ActivatePlugins)result!;
		Assert.Multiple(() =>
		{
			Assert.That(activatePlugins.Activate, Is.True);
			Assert.That(activatePlugins.Plugins, Is.EqualTo(new[] { "a.esp" }));
		});
	}

	[Test]
	public void Deserialize_UnknownProperty_IgnoresIt()
	{
		// Arrange
		const string json = """{"$type":"InstallMod","DownloadInfo":{"S3Path":"a.7z"},"ModName":"A","FutureFlag":true}""";

		// Act
		var result = JsonSerializer.Deserialize(json, SourceGeneratedJsonContext.Default.InstallAction);

		// Assert
		Assert.That(result, Is.TypeOf<InstallAction.InstallMod>()
			.With.Property(nameof(InstallAction.InstallMod.ModName)).EqualTo("A")
			.And.Property(nameof(InstallAction.InstallMod.DownloadInfo))
			.With.Property(nameof(DownloadInfo.S3Path)).EqualTo("a.7z"));
	}

	[Test]
	public void Deserialize_RemoveFileSystemItemWithoutFailOnFailedDelete_FailsOnFailedDelete()
	{
		// Arrange
		const string json = """{"$type":"RemoveFileSystemItem","Paths":["mods/a"]}""";

		// Act
		var result = JsonSerializer.Deserialize(json, SourceGeneratedJsonContext.Default.InstallAction);

		// Assert
		Assert.That(result, Is.TypeOf<InstallAction.RemoveFileSystemItem>()
			.With.Property(nameof(InstallAction.RemoveFileSystemItem.FailOnFailedDelete)).True);
	}

	[TestCase("""{"$type":"UninstallMod","Name":"a"}""", TestName = "Deserialize_UnknownType_Throws")]
	[TestCase("""{"$type":"installMod","ArchiveName":"a"}""", TestName = "Deserialize_TypeInOtherCase_Throws")]
	[TestCase("""{"ArchiveName":"a"}""", TestName = "Deserialize_MissingType_Throws")]
	[TestCase("""{"DownloadInfo":{"S3Path":"a.7z"},"ModName":"a","$type":"InstallMod"}""", TestName = "Deserialize_TypeNotFirst_Throws")]
	[TestCase("""{"$type":"InstallMod"}""", TestName = "Deserialize_MissingCaseProperty_Throws")]
	[TestCase("""{"$type":"InstallMod","DownloadInfo":{"S3Path":"a.7z"}}""", TestName = "Deserialize_InstallModWithoutModName_Throws")]
	[TestCase("""{"InstallMod":{"ArchiveName":"a"}}""", TestName = "Deserialize_FeedV10Format_Throws")]
	[TestCase("{}", TestName = "Deserialize_EmptyObject_Throws")]
	[TestCase("\"InstallMod\"", TestName = "Deserialize_String_Throws")]
	public void Deserialize_InvalidJson_Throws(string json)
	{
		// Act & Assert
		Assert.That(
			() => JsonSerializer.Deserialize(json, SourceGeneratedJsonContext.Default.InstallAction),
			Throws.InstanceOf<JsonException>().Or.InstanceOf<NotSupportedException>());
	}

	private static TestCaseData Case(InstallAction action, string json) =>
		new TestCaseData(action, json).SetArgDisplayNames(action.GetType().Name);
}
