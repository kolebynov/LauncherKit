using System.Text.Json;
using System.Text.Json.Nodes;
using LauncherKit.Core;
using LauncherKit.FeedEditor.Feed;
using LauncherKit.FeedEditor.Feed.Json;

namespace LauncherKit.FeedEditor.Tests.Feed.Json;

[TestFixture]
public sealed class FeedJsonTests
{
	private const string Hash = "9DBD20E17BD455D5377BE57692578FDDF9D6D64109AC7280A9A9636CA7E09444";

	[Test]
	public void Serialize_StoredFeed_LauncherReadsSameFeed()
	{
		// Arrange
		var storedJson = FeedFixtures.StoredFeedJson();

		// Act
		var result = FeedJson.Serialize(FeedJson.Deserialize(storedJson));

		// Assert
		Assert.That(ThroughLauncher(result), Is.EqualTo(ThroughLauncher(storedJson)));
	}

	[Test]
	public void Serialize_BetaRolesAndSponsorBeta_LauncherReadsRoleIdsAndFlag()
	{
		// Arrange
		var feed = FeedJson.Deserialize(FeedFixtures.StoredFeedJson());
		feed.SponsorRoleIds.Add("197038439483310086");
		feed.DeveloperRoleIds.Add("41771983423143936");
		feed.Modpacks[0].IsAvailableForSponsors = true;

		// Act
		var result = FeedJson.Serialize(feed);

		// Assert
		var feedData = JsonSerializer.Deserialize(result, SourceGeneratedJsonContext.Default.FeedData)!;
		Assert.Multiple(() =>
		{
			Assert.That(JsonNode.Parse(result)!["SponsorRoleIds"]![0]!.GetValueKind(), Is.EqualTo(JsonValueKind.String));
			Assert.That(JsonNode.Parse(result)!["DeveloperRoleIds"]![0]!.GetValueKind(), Is.EqualTo(JsonValueKind.String));
			Assert.That(feedData.SponsorRoleIds, Is.EqualTo(new ulong[] { 197038439483310086 }));
			Assert.That(feedData.DeveloperRoleIds, Is.EqualTo(new ulong[] { 41771983423143936 }));
			Assert.That(feedData.Modpacks[0].IsAvailableForSponsors, Is.True);
		});
	}

	[Test]
	public void Serialize_NoBetaRoles_OmitsThemAndLauncherReadsNone()
	{
		// Arrange
		var feed = FeedJson.Deserialize(FeedFixtures.StoredFeedJson());
		feed.SponsorRoleIds.Clear();
		feed.DeveloperRoleIds.Clear();

		// Act
		var result = FeedJson.Serialize(feed);

		// Assert
		var feedData = JsonSerializer.Deserialize(result, SourceGeneratedJsonContext.Default.FeedData)!;
		Assert.Multiple(() =>
		{
			Assert.That(JsonNode.Parse(result)!.AsObject().ContainsKey("SponsorRoleIds"), Is.False);
			Assert.That(JsonNode.Parse(result)!.AsObject().ContainsKey("DeveloperRoleIds"), Is.False);
			Assert.That(feedData.SponsorRoleIds, Is.Null);
			Assert.That(feedData.DeveloperRoleIds, Is.Null);
		});
	}

	[Test]
	public void Serialize_EveryCase_LauncherReadsEveryValue()
	{
		// Arrange
		var feed = new FeedModel
		{
			DefaultLanguage = "ru",
			SkyrimDownloadInfo = new DownloadInfoModel { S3Path = "Skyrim.7z", Hash = Hash },
			SevenZipDownloadInfo = new DownloadInfoModel { S3Path = "7z.zip" },
			Launchers =
			{
				new LauncherModel
				{
					Version = "1.20.0", DownloadInfo = new DownloadInfoModel { S3Path = "l.exe" }, ChangelogPath = Text("c.md"), SupportsNewModpackVersions = true,
				},
			},
			BetaVersions = { new BetaVersionModel { Hash = Hash, VersionRange = "[3.2.0-beta.1, 3.2.0)" } },
			Modpacks =
			{
				new ModpackModel
				{
					InitialVersion = "3.1.0",
					Version = "3.1.0",
					DownloadInfo = new DownloadInfoModel { S3Path = "stb.7z", Hash = Hash },
					Patches =
					{
						new ModpackPatchModel
						{
							Version = "3.1.1",
							DownloadInfo = new DownloadInfoModel { S3Path = "patch.7z" },
							RequireNewGame = true,
							InstallSteps = { new ModInstallStepModel { Actions = { new RemoveFileSystemItemAction { Paths = { "a", "b" } } } } },
						},
					},
				},
			},
			GraphPacks =
			{
				new GraphPackModel
				{
					Name = "Graph pack",
					DisplayName = Text("Графпак"),
					LogoPath = "gp.jpg",
					DescriptionPath = Text("gp.md"),
					Recommendation = Text("8 GB"),
					IsDefault = true,
					IsDiscontinued = true,
					Items =
					{
						new ModItemModel
						{
							Version = "1.0.0",
							ArchiveDownloadInfo = new DownloadInfoModel { S3Path = "gp/part1.7z", Hash = Hash },
							SupportedModpackVersion = "[3.1.0, 3.2.0)",
							SupportsUninstall = true,
							InstallSteps =
							{
								new ModInstallStepModel
								{
									Actions = { new ExtractArchiveAction { DownloadInfo = new DownloadInfoModel { S3Path = "gp/part2.7z" } } },
								},
							},
						},
					},
				},
			},
			Mods =
			{
				new ModModel
				{
					Name = "Mod",
					DisplayName = Text("Мод"),
					LogoPath = "logo.jpg",
					DescriptionPath = Text("description.md"),
					Items =
					{
						new ModItemModel
						{
							Version = "1.0.0",
							ArchiveDownloadInfo = new DownloadInfoModel { S3Path = "Mods/mod.7z", Hash = Hash },
							Placement = new ModPlacementModel { Kind = ModPlacementKind.UnderSeparator, Separator = "Ваши моды" },
							SupportedModpackVersion = "[3.1.0, 3.2.0)",
							SupportsUninstall = true,
							Patches =
							{
								new ModPatchModel
								{
									Version = "1.0.1",
									DownloadInfo = new DownloadInfoModel { S3Path = "Mods/mod-patch.7z", Hash = Hash },
									InstallSteps = { new ModInstallStepModel { Actions = { new RemoveFileSystemItemAction { Paths = { "obsolete" }, FailOnFailedDelete = false } } } },
								},
							},
							InstallSteps =
							{
								new ModInstallStepModel
								{
									CanRollback = true,
									Actions =
									{
										new InstallModAction
										{
											DownloadInfo = new DownloadInfoModel { S3Path = "Mods/b.7z", Hash = Hash }, ModName = "B",
											Placement = new ModPlacementModel { Kind = ModPlacementKind.NextToMod, ModName = "A", Side = ModListSide.Above },
										},
										new SelectRequiredModAction { Mods = { "A", "B" } },
										new ActivateModsAction { Activate = false, Mods = { "A" } },
										new ActivatePluginsAction { Activate = true, Plugins = { "a.esp" } },
										new RemoveFileSystemItemAction { Paths = { "old" } },
										new ExtractArchiveAction { DownloadInfo = new DownloadInfoModel { S3Path = "Mods/a.7z", Hash = Hash }, DestinationPath = "dest" },
										new MergePluginsAction { DownloadInfo = new DownloadInfoModel { S3Path = "Mods/plugins [1.0.0].txt", Hash = Hash } },
										new ChangeProfileAction { ProfileName = "Profile" },
									},
								},
								new ModInstallStepModel
								{
									Actions =
									{
										new InstallModAction
										{
											DownloadInfo = new DownloadInfoModel { S3Path = "Mods/c.7z" }, ModName = "C",
											Placement = new ModPlacementModel { Kind = ModPlacementKind.NextToMod, ModName = "B", Side = ModListSide.Below },
										},
									},
									Condition = new ModInstallConditionModel { StepIndex = 0, Operator = CompareOperator.NotEqual, Value = "A" },
								},
							},
						},
						new ModItemModel
						{
							Version = "0.9.0",
							ArchiveDownloadInfo = new DownloadInfoModel { S3Path = "Mods/mod-0.9.7z" },
							SupportedModpackVersion = "[3.0.0, 3.1.0)",
						},
					},
				},
			},
		};

		// Act
		var result = FeedJson.Serialize(feed);

		// Assert
		var expected = WithoutWriteOnlyValues(JsonNode.Parse(result));
		var actual = WithoutWriteOnlyValues(JsonNode.Parse(ThroughLauncher(result)));
		Assert.That(JsonNode.DeepEquals(actual, expected), Is.True, $"Launcher read:\n{actual}\nEditor wrote:\n{expected}");
	}

	[Test]
	public void Serialize_EveryCase_WritesTypeFirst()
	{
		// Arrange
		var feed = new FeedModel
		{
			DefaultLanguage = "ru",
			Mods =
			{
				new ModModel
				{
					Name = "M",
					Items =
					{
						new ModItemModel
						{
							ArchiveDownloadInfo = new DownloadInfoModel { S3Path = "Mods/m.7z" },
							InstallSteps =
							{
								new ModInstallStepModel
								{
									Actions =
									{
										new InstallModAction { DownloadInfo = new DownloadInfoModel { S3Path = "b.7z", Hash = Hash }, ModName = "B" },
										new ExtractArchiveAction { DownloadInfo = new DownloadInfoModel { S3Path = "a.7z" }, DestinationPath = "dest" },
									},
								},
							},
						},
					},
				},
			},
		};

		// Act
		var result = JsonNode.Parse(FeedJson.Serialize(feed))!;

		// Assert
		var mods = result["Mods"]!.AsArray();
		var actions = mods[0]!["Items"]![0]!["InstallSteps"]![0]!["Actions"]!.AsArray();
		Assert.Multiple(() =>
		{
			Assert.That(actions[0]!.ToJsonString(), Is.EqualTo($$"""{"$type":"InstallMod","DownloadInfo":{"S3Path":"b.7z","Hash":"{{Hash}}"},"ModName":"B"}"""));
			Assert.That(actions[1]!.ToJsonString(), Is.EqualTo("""{"$type":"ExtractArchive","DownloadInfo":{"S3Path":"a.7z"},"DestinationPath":"dest"}"""));
		});
	}

	[Test]
	public void Serialize_UnknownProperties_WrittenBackUnchanged()
	{
		// Arrange
		var json = """
			{
				"FutureRootSetting": { "Enabled": true },
				"SkyrimDownloadInfo": { "S3Path": "Skyrim.7z", "FutureSize": 42 },
				"Mods": [{
					"Name": "Mod", "LogoPath": "l", "DescriptionPath": {"ru":"d"},
					"Items": [{ "Version": "1.0.0", "ArchiveDownloadInfo": { "S3Path": "Mods/mod.7z" }, "SupportedModpackVersion": "[1.0.0, 2.0.0)", "InstallSteps": [{
						"Actions": [{ "$type": "InstallMod", "DownloadInfo": { "S3Path": "Mods/a.7z", "FutureSize": 7 }, "ModName": "A", "FutureFlag": "x" }]
					}]}]
				}]
			}
			""";

		// Act
		var result = JsonNode.Parse(FeedJson.Serialize(FeedJson.Deserialize(json)))!;

		// Assert
		var action = result["Mods"]?[0]?["Items"]?[0]?["InstallSteps"]?[0]?["Actions"]?[0];
		Assert.Multiple(() =>
		{
			Assert.That(result["FutureRootSetting"]?["Enabled"]?.GetValue<bool>(), Is.True);
			Assert.That(result["SkyrimDownloadInfo"]?["FutureSize"]?.GetValue<int>(), Is.EqualTo(42));
			Assert.That(action?.ToJsonString(), Is.EqualTo("""{"$type":"InstallMod","DownloadInfo":{"S3Path":"Mods/a.7z","FutureSize":7},"ModName":"A","FutureFlag":"x"}"""));
		});
	}

	[TestCase("""{ "$type": "UninstallMod", "Name": "a" }""", TestName = "Deserialize_InstallActionWithUnknownType_ThrowsJsonException")]
	[TestCase("""{ "ModName": "a" }""", TestName = "Deserialize_InstallActionWithoutType_ThrowsJsonException")]
	[TestCase("""{ "ModName": "a", "$type": "InstallMod" }""", TestName = "Deserialize_InstallActionWithTypeNotFirst_ThrowsJsonException")]
	[TestCase("""{ "$type": 1, "ModName": "a" }""", TestName = "Deserialize_InstallActionWithNonStringType_ThrowsJsonException")]
	[TestCase("""{ "InstallMod": { "ModName": "a" } }""", TestName = "Deserialize_InstallActionInFeedV10Format_ThrowsJsonException")]
	[TestCase("""["InstallMod"]""", TestName = "Deserialize_InstallActionArray_ThrowsJsonException")]
	public void Deserialize_InvalidInstallAction_ThrowsJsonException(string action)
	{
		// Arrange
		var json = FeedWithMod(action);

		// Act & Assert
		Assert.That(() => FeedJson.Deserialize(json), Throws.InstanceOf<JsonException>());
	}

	[Test]
	public void Deserialize_ExplicitNullCollections_ReadsThemAsEmpty()
	{
		// Arrange
		var json = """
			{
				"Modpacks": [{
					"InitialVersion": "3.0.0", "Version": "3.1.0", "DownloadInfo": { "S3Path": "stb.7z" },
					"Patches": [{ "Version": "3.1.0", "DownloadInfo": { "S3Path": "p.7z" }, "InstallSteps": null }]
				}, {
					"InitialVersion": "4.0.0", "Version": "4.0.0", "DownloadInfo": { "S3Path": "stb4.7z" }, "Patches": null
				}],
				"InstallComponents": null,
				"Prerequisites": null,
				"ModpackChangelogs": null,
				"GraphPacks": null,
				"Mods": null,
				"BetaVersions": null
			}
			""";

		// Act
		var result = FeedJson.Deserialize(json);

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(result.Modpacks[0].Patches[0].InstallSteps, Is.Empty);
			Assert.That(result.Modpacks[1].Patches, Is.Empty);
			Assert.That(result.GraphPacks, Is.Empty);
			Assert.That(result.Mods, Is.Empty);
			Assert.That(result.BetaVersions, Is.Empty);
			Assert.That(result.InstallComponents, Is.Empty);
			Assert.That(result.Prerequisites, Is.Empty);
			Assert.That(result.ModpackChangelogs, Is.Empty);
		});
	}

	[TestCase("", TestName = "Deserialize_ModItemWithoutInstallSteps_ReadsThemAsEmpty")]
	[TestCase(""", "InstallSteps": null""", TestName = "Deserialize_ModItemWithNullInstallSteps_ReadsThemAsEmpty")]
	[TestCase(""", "InstallSteps": []""", TestName = "Deserialize_ModItemWithEmptyInstallSteps_ReadsThemAsEmpty")]
	public void Deserialize_ModItemWithoutSteps_ReadsThemAsEmpty(string installSteps)
	{
		// Arrange
		var json = $$"""
			{ "Mods": [{ "Name": "M", "LogoPath": "l", "DescriptionPath": {"ru":"d"}, "Items": [{
				"Version": "1.0.0", "ArchiveDownloadInfo": { "S3Path": "Mods/m.7z" }, "SupportedModpackVersion": "[1.0.0, 2.0.0)"{{installSteps}} }] }] }
			""";

		// Act
		var result = FeedJson.Deserialize(json);

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(result.Mods[0].Items[0].InstallSteps, Is.Empty);
			Assert.That(result.Mods[0].Items[0].ArchiveDownloadInfo.S3Path, Is.EqualTo("Mods/m.7z"));
		});
	}

	[Test]
	public void Serialize_ModItemWithoutSteps_OmitsInstallSteps()
	{
		// Arrange
		var feed = new FeedModel
		{
			DefaultLanguage = "ru",
			Mods =
			{
				new ModModel
				{
					Name = "M",
					Items = { new ModItemModel { Version = "1.0.0", ArchiveDownloadInfo = new DownloadInfoModel { S3Path = "Mods/m.7z" } } },
				},
			},
		};

		// Act
		var result = JsonNode.Parse(FeedJson.Serialize(feed))!;

		// Assert
		var item = result["Mods"]![0]!["Items"]![0]!.AsObject();
		Assert.Multiple(() =>
		{
			Assert.That(item.ContainsKey("InstallSteps"), Is.False);
			Assert.That(item["ArchiveDownloadInfo"]!.ToJsonString(), Is.EqualTo("""{"S3Path":"Mods/m.7z"}"""));
		});
	}

	[Test]
	public void Serialize_NullLocalizedOptionalTextAndBlankOptionalHash_OmitsProperties()
	{
		// Arrange
		var feed = new FeedModel
		{
			DefaultLanguage = "ru",
			SkyrimDownloadInfo = new DownloadInfoModel { S3Path = "Skyrim.7z", Hash = "  " },
			Launchers = { new LauncherModel { Version = "1.0.0", DownloadInfo = new DownloadInfoModel { S3Path = "l.exe" }, ChangelogPath = null } },
			Mods = { new ModModel { Name = "M", DisplayName = null } },
			GraphPacks = { new GraphPackModel { Name = "G", DisplayName = null } },
		};

		// Act
		var result = JsonNode.Parse(FeedJson.Serialize(feed))!;

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(result["SkyrimDownloadInfo"]!.AsObject().ContainsKey("Hash"), Is.False);
			Assert.That(result["Launchers"]![0]!.AsObject().ContainsKey("ChangelogPath"), Is.False);
			Assert.That(result["Mods"]![0]!.AsObject().ContainsKey("DisplayName"), Is.False);
			Assert.That(result["GraphPacks"]![0]!.AsObject().ContainsKey("DisplayName"), Is.False);
		});
	}

	[Test]
	public void Clone_ModItem_ReturnsIndependentDeepCopy()
	{
		// Arrange
		var original = new ModItemModel
		{
			Version = "1.0.0",
			ArchiveDownloadInfo = new DownloadInfoModel { S3Path = "Mods/a.7z" },
			InstallSteps = { new ModInstallStepModel { Actions = { new ActivateModsAction { Mods = { "A" } } } } },
		};

		// Act
		var clone = FeedJson.Clone(original);
		clone.Version = "2.0.0";
		clone.ArchiveDownloadInfo.S3Path = "Mods/b.7z";
		((ActivateModsAction)clone.InstallSteps[0].Actions[0]).Mods.Add("B");

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(original.Version, Is.EqualTo("1.0.0"));
			Assert.That(original.ArchiveDownloadInfo.S3Path, Is.EqualTo("Mods/a.7z"));
			Assert.That(((ActivateModsAction)original.InstallSteps[0].Actions[0]).Mods, Is.EqualTo(new[] { "A" }));
		});
	}

	/// <summary>Reads JSON with the launcher's own model and writes it back the way the launcher sees it.</summary>
	private static string ThroughLauncher(string json)
	{
		var feedData = JsonSerializer.Deserialize(json, SourceGeneratedJsonContext.Default.FeedData);
		return JsonSerializer.Serialize(feedData, SourceGeneratedJsonContext.Default.FeedData);
	}

	private static Dictionary<string, string> Text(string value) => new() { ["ru"] = value };

	private static string FeedWithMod(string action) => $$"""
		{ "Mods": [{ "Name": "M", "LogoPath": "l", "DescriptionPath": {"ru":"d"}, "Items": [{
			"Version": "1.0.0", "ArchiveDownloadInfo": { "S3Path": "Mods/m.7z" }, "SupportedModpackVersion": "[1.0.0, 2.0.0)",
			"InstallSteps": [{ "Actions": [{{action}}] }] }] }] }
		""";

	/// <summary>
	/// Drops what only the launcher's writer produces: nulls for absent optional values, which the editor omits, and
	/// the computed <c>DownloadInfo.FileName</c>, which the launcher never reads.
	/// </summary>
	private static JsonNode? WithoutWriteOnlyValues(JsonNode? node)
	{
		switch (node)
		{
			case JsonObject jsonObject:
				foreach (var property in jsonObject.Where(x => x.Value is null || x.Key == "FileName").Select(x => x.Key).ToArray())
				{
					jsonObject.Remove(property);
				}

				foreach (var property in jsonObject)
				{
					WithoutWriteOnlyValues(property.Value);
				}

				break;
			case JsonArray jsonArray:
				foreach (var item in jsonArray)
				{
					WithoutWriteOnlyValues(item);
				}

				break;
		}

		return node;
	}
}
