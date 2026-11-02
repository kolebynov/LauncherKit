using System.Globalization;
using LauncherKit.Desktop.Avalonia.Extensions;
using LauncherKit.Desktop.Extensions;
using LauncherKit.Desktop.Localization;
using System.Resources;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using NuGet.Versioning;
using LauncherKit.Core;
using LauncherKit.Core.AppTasks;
using LauncherKit.Core.AppTasks.MicroTasks;
using LauncherKit.Core.Choices;
using LauncherKit.Core.Exceptions;
using LauncherKit.Core.FileStorage;
using LauncherKit.Core.UserSettings;
using LauncherKit.Core.Feed;
using LauncherKit.Core.Utils;
using LauncherKit.Infrastructure;

namespace LauncherKit.Desktop.Avalonia.Tests.Localization;

[TestFixture]
[NonParallelizable]
public sealed class DescriptionTextsTests
{
	[TestCase("en-US")]
	[TestCase("ru-RU")]
	public void EveryDescription_ResolvesExistingResourceAndFormatsArguments(string language)
	{
		using var provider = new ServiceCollection().AddLogging().AddLauncherKitLocalization().BuildServiceProvider();
		var originalUi = CultureInfo.CurrentUICulture;
		var originalCulture = CultureInfo.CurrentCulture;
		try
		{
			CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language);
			CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(language);
			var tasks = provider.GetRequiredService<TaskTexts>();
			var choices = provider.GetRequiredService<ChoiceTexts>();
			var version = new NuGetVersion("2.4.6");
			AppTaskDescription[] descriptions =
			[
				new AppTaskDescription.InstallModpack(version),
				new AppTaskDescription.InstallModpackPatch(version),
				AppTaskDescription.UninstallModpack.Instance,
				new AppTaskDescription.InstallModpackComponent("id", new LocalizedText { ["en"] = "component-marker", ["ru"] = "component-marker" }),
				new AppTaskDescription.InstallMod("mod-marker"),
				new AppTaskDescription.UninstallMod("mod-marker"),
				new AppTaskDescription.UpdateLauncher(version),
				new AppTaskDescription.RunGame(StartGameVariant.Game),
				AppTaskDescription.DeleteOldSaves.Instance,
				AppTaskDescription.InstallSkyrim.Instance,
				AppTaskDescription.SelectModpackComponents.Instance,
				AppTaskDescription.InstallSevenZip.Instance,
				new AppTaskDescription.InstallVcRedist("architecture-marker"),
				AppTaskDescription.CleanupOldData.Instance,
			];
			foreach (var description in descriptions) AssertResolved(tasks.Get(description));
			MicroTaskDescription[] operations =
			[
				MicroTaskDescription.Preparing.Instance,
				new MicroTaskDescription.DownloadFile("argument-marker"),
				new MicroTaskDescription.ExtractArchive("argument-marker"),
				new MicroTaskDescription.InstallMod("argument-marker"),
				new MicroTaskDescription.MergePlugins("argument-marker"),
				MicroTaskDescription.DeleteFiles.Instance,
				MicroTaskDescription.SelectRequiredMod.Instance,
				MicroTaskDescription.SelectModpackComponents.Instance,
				new MicroTaskDescription.RunProcess("argument-marker"),
				new MicroTaskDescription.GameStarted(StartGameVariant.Game),
				MicroTaskDescription.RunLauncherSetup.Instance,
				MicroTaskDescription.DeleteModOrganizer.Instance,
				MicroTaskDescription.DeleteOldSaves.Instance,
				MicroTaskDescription.ExtractModpackPatch.Instance,
				MicroTaskDescription.UpdateModpackVersion.Instance,
				MicroTaskDescription.DeletePreviousVersion.Instance,
				MicroTaskDescription.DeleteUnneededFiles.Instance,
				MicroTaskDescription.ExtractModpack.Instance,
				new MicroTaskDescription.InstallPrerequisite("argument-marker"),
				MicroTaskDescription.InstallSkyrim.Instance,
				MicroTaskDescription.DeleteBackups.Instance,
				MicroTaskDescription.InstallSevenZip.Instance,
				MicroTaskDescription.Installing.Instance,
				MicroTaskDescription.PreparingInstallation.Instance,
				MicroTaskDescription.FinishInstallation.Instance,
				new MicroTaskDescription.ExtractModPatch(version),
				new MicroTaskDescription.UninstallMod("argument-marker"),
				MicroTaskDescription.UpdateModMetadata.Instance,
				MicroTaskDescription.DeleteOldDownloads.Instance,
				MicroTaskDescription.DeleteTemporaryFiles.Instance,
				new MicroTaskDescription.RestoreFile("argument-marker"),
				MicroTaskDescription.RestoreFiles.Instance,
				new MicroTaskDescription.RevertArchiveExtraction("argument-marker"),
				new MicroTaskDescription.ChangeProfile("argument-marker"),
				new MicroTaskDescription.RevertPluginsMerge("argument-marker"),
				new MicroTaskDescription.DeleteFile("argument-marker"),
				new MicroTaskDescription.BackupArchiveFiles("argument-marker"),
				MicroTaskDescription.CheckFileHash.Instance,
				MicroTaskDescription.PrepareFile.Instance,
				MicroTaskDescription.ConfigureMod.Instance,
				MicroTaskDescription.GetDownloadSizes.Instance,
				new MicroTaskDescription.ActivatePlugins(true), new MicroTaskDescription.ActivatePlugins(false),
				new MicroTaskDescription.ActivateMods(true), new MicroTaskDescription.ActivateMods(false),
			];
			foreach (var operation in operations) AssertResolved(tasks.Get(operation));
			ChoiceText[] choiceDescriptions =
			[
				ChoiceText.NoneOption.Instance, ChoiceText.YesOption.Instance, ChoiceText.GraphPackStepTitle.Instance,
				new ChoiceText.ExtraDownloadSize(1536), new ChoiceText.SelectRequiredModTitle(["mod-marker"]),
				ChoiceText.RequiredModsStepTitle.Instance, ChoiceText.SelectOneModSubtitle.Instance,
				ChoiceText.UnavailableGraphPackUpdating.Instance, ChoiceText.UnavailableFomodOption.Instance,
				ChoiceText.ModpackComponentsTitle.Instance,
				new ChoiceText.ComponentOptionDescription("name-marker", "recommendation-marker", 1536, true),
			];
			foreach (var choice in choiceDescriptions) AssertResolved(choices.Get(choice));
			Assert.Multiple(() =>
			{
				Assert.That(tasks.Get(new AppTaskDescription.InstallModpack(version)).Value, Does.Contain(version.ToString()));
				Assert.That(tasks.Get(new MicroTaskDescription.DownloadFile("argument-marker")).Value, Does.Contain("argument-marker"));
				Assert.That(choices.Get(new ChoiceText.ExtraDownloadSize(1536)).Value,
					Does.Contain(BytesFormatter.Format(1536, BytesFormatter.Measurement.Size.Instance)));
				Assert.That(choices.Get(new ChoiceText.SelectRequiredModTitle(["first-marker", "second-marker"])).Value,
					Does.Contain("first-marker").And.Contain("second-marker"));
				Assert.That(tasks.Get(new AppTaskDescription.Custom("custom-marker")).Value, Is.EqualTo("custom-marker"));
				Assert.That(tasks.Get(new MicroTaskDescription.Custom("custom-marker")).Value, Is.EqualTo("custom-marker"));
				Assert.That(choices.Get(new ChoiceText.Content("content-marker")).Value, Is.EqualTo("content-marker"));
			});
		}
		finally
		{
			CultureInfo.CurrentUICulture = originalUi;
			CultureInfo.CurrentCulture = originalCulture;
		}
	}

	[TestCase("DesktopStrings")]
	public void EveryNeutralKey_ExistsInRussianWithoutParentFallback(string bundle)
	{
		var resources = new ResourceManager($"LauncherKit.Desktop.Localization.{bundle}", typeof(DesktopStrings).Assembly);
		var neutral = resources.GetResourceSet(CultureInfo.InvariantCulture, true, false)!;
		var russian = resources.GetResourceSet(CultureInfo.GetCultureInfo("ru"), true, false)!;
		foreach (System.Collections.DictionaryEntry entry in neutral)
		{
			Assert.That(russian.GetString((string)entry.Key), Is.Not.Null.And.Not.Empty, $"Missing Russian key: {entry.Key}");
		}
	}

	[TestCase("en")]
	[TestCase("ru")]
	public void EveryKnownExceptionAndEnum_ResolvesExistingResource(string language)
	{
		var previous = CultureInfo.CurrentUICulture;
		try
		{
			CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language);
			using var provider = new ServiceCollection().AddLogging().AddLauncherKitLocalization().BuildServiceProvider();
			var exceptions = provider.GetRequiredService<ExceptionTexts>();
			var inner = new IOException("detail-marker");
			Exception[] values =
			[
				new FileSystemItemDeletionException("path-marker", inner),
				new ModDeletionException("mod-marker", inner),
				new SaveDeletionException("save-marker", inner),
				new LocalSavesDisabledException("profile-marker"),
				new DuplicatePluginException("plugin-marker"),
				new StoredFileInUseException("path-marker"),
				new StoredFileAccessDeniedException("path-marker"),
				new StoredFileDeletionException("path-marker", inner),
				new NotEnoughSpaceException("drive-marker", 2048, 1024),
				new ModpackNotInstalledException(),
				new ModNotSupportedModpackVersionException("mod-marker", new NuGetVersion("1.2.3")),
				new ModOrganizerAlreadyRunningException(),
				new DownloadStalledException(TimeSpan.FromSeconds(10)),
				new InvalidOperationException("diagnostic-marker"),
			];
			foreach (var exception in values) AssertResolved(exceptions.Get(exception));
			var enums = provider.GetRequiredService<EnumTexts>();
			foreach (var period in FileStoragePeriodExtensions.GetValues()) AssertResolved(enums.Get(period));
			foreach (var status in StoredFileStatusExtensions.GetValues()) AssertResolved(enums.Get(status));
		}
		finally
		{
			CultureInfo.CurrentUICulture = previous;
		}
	}

	[TestCase(false, null, true)]
	[TestCase(false, 1536L, true)]
	[TestCase(true, null, true)]
	[TestCase(true, 1536L, false)]
	public void ComponentDescriptions_PreserveNameRecommendationAndDownloadArguments(bool isMod, long? bytes, bool includesName)
	{
		using var provider = new ServiceCollection().AddLogging().AddLauncherKitLocalization().BuildServiceProvider();
		var mapper = provider.GetRequiredService<ChoiceTexts>();
		var description = mapper.Get(new ChoiceText.ComponentOptionDescription("name-marker", "recommendation-marker", bytes, isMod));
		AssertResolved(description);
		Assert.That(description.Value, Does.Contain("recommendation-marker"));
		Assert.That(description.Value.Contains("name-marker", StringComparison.Ordinal), Is.EqualTo(includesName));
		if (bytes is { } count)
			Assert.That(description.Value, Does.Contain(BytesFormatter.Format(count, BytesFormatter.Measurement.Size.Instance)));
	}

	private static void AssertResolved(LocalizedString value)
	{
		Assert.Multiple(() =>
		{
			Assert.That(value.ResourceNotFound, Is.False, value.Name);
			Assert.That(value.Value, Is.Not.Empty.And.Not.Contains("{0}").And.Not.Contains("{1}"), value.Name);
		});
	}
}
