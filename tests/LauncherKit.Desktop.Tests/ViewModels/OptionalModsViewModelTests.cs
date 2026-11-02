using System.ComponentModel;
using LauncherKit.Desktop.Services;
using LauncherKit.Desktop.ViewModels;
using NuGet.Versioning;
using LauncherKit.Core.AppTasks;
using LauncherKit.Core.Feed;
using LauncherKit.Core.Feed.Mods;
using LauncherKit.Core.Models;
using LauncherKit.Core.Services.Abstractions;
using LauncherKit.Core.Utils;
using LauncherKit.Infrastructure;
using LauncherKit.TestSupport;

namespace LauncherKit.Desktop.Tests.ViewModels;

[NonParallelizable]
[TestFixture]
public sealed class OptionalModsViewModelTests
{
	[Test]
	public async Task Activate_MixedCompatibility_ShowsOnlySupportedMods()
	{
		var compatible = CreateMod("Compatible", "[2.0.0,3.0.0)");
		var incompatible = CreateMod("Incompatible", "[3.0.0,4.0.0)");
		var mocker = new AutoMocker(MockBehavior.Strict);
		mocker.Use<IUiDispatcher>(new HomeCardsDispatcher());
		var modpackVersionProvider = mocker.GetMock<IModpackVersionProvider>();
		modpackVersionProvider.SetupGet(x => x.InstalledModpackVersion).Returns(NuGetVersion.Parse("2.5.0"));
		modpackVersionProvider.SetupAdd(x => x.PropertyChanged += It.IsAny<PropertyChangedEventHandler>());
		modpackVersionProvider.SetupRemove(x => x.PropertyChanged -= It.IsAny<PropertyChangedEventHandler>());
		mocker.GetMock<IFeedProvider>()
			.Setup(x => x.GetModFeedDatas(It.IsAny<CancellationToken>()))
			.Returns(() => ValueTask.FromResult<IReadOnlyList<ModFeedData>?>([compatible, incompatible]));
		mocker.GetMock<IFeedUtils>().Setup(x => x.GetUri("compatible.png")).Returns(new Uri("https://feed.test/compatible.png"));
		mocker.GetMock<IFeedUtils>().Setup(x => x.GetUri("compatible.md")).Returns(new Uri("https://feed.test/compatible.md"));
		mocker.GetMock<IInstallAppTaskProvider>()
			.Setup(x => x.GetModInstallState(compatible, NuGetVersion.Parse("2.5.0")))
			.Returns(ModInstallState.NotInstalled);
		var sut = mocker.CreateInstance<OptionalModsViewModel>();

		using var activation = await sut.Activator.Activate(CancellationToken.None);
		modpackVersionProvider.Raise(
			x => x.PropertyChanged += null,
			new PropertyChangedEventArgs(nameof(IModpackVersionProvider.InstalledModpackVersion)));
		await TaskAssert.Eventually(() => sut.OptionalMods.Count == 1, "compatible mod projection");

		Assert.That(sut.OptionalMods.Single().Name, Is.EqualTo("Compatible"));
		Assert.That(sut.OptionalMods.Single().DescriptionUri, Is.EqualTo(new Uri("https://feed.test/compatible.md")));
		Assert.That(sut.OptionalMods.Single().InstallRequiresNewGame, Is.False);
		Assert.That(sut.OptionalMods.Single().UninstallRequiresNewGame, Is.False);
	}

	[Test]
	public async Task Card_InstallAndUninstall_QueueTasksWithoutUiServices()
	{
		var mod = CreateMod("Optional", "[2.0.0,3.0.0)");
		var version = NuGetVersion.Parse("2.5.0");
		var state = ModInstallState.NotInstalled;
		var mocker = new AutoMocker(MockBehavior.Strict);
		mocker.Use<IUiDispatcher>(new HomeCardsDispatcher());
		var provider = mocker.GetMock<IModpackVersionProvider>();
		provider.SetupGet(x => x.InstalledModpackVersion).Returns(version);
		provider.SetupAdd(x => x.PropertyChanged += It.IsAny<PropertyChangedEventHandler>());
		provider.SetupRemove(x => x.PropertyChanged -= It.IsAny<PropertyChangedEventHandler>());
		mocker.GetMock<IFeedProvider>().Setup(x => x.GetModFeedDatas(It.IsAny<CancellationToken>())).ReturnsAsync([mod]);
		mocker.GetMock<IFeedUtils>().Setup(x => x.GetUri(It.IsAny<string>())).Returns<string>(p => new Uri("https://feed.test/" + p));
		mocker.GetMock<IInstallAppTaskProvider>().Setup(x => x.GetModInstallState(mod, version)).Returns(() => state);
		var install = new AppTask("Install", new AppTaskDescription.Custom("Install"), _ => [], [], null);
		var uninstall = new AppTask("Uninstall", new AppTaskDescription.Custom("Uninstall"), _ => [], [], null);
		mocker.GetMock<IInstallAppTaskProvider>().Setup(x => x.CreateModInstallAppTask(mod, null, It.IsAny<CancellationToken>())).ReturnsAsync(install);
		mocker.GetMock<IInstallAppTaskProvider>().Setup(x => x.CreateModUninstallAppTask(mod)).Returns(uninstall);
		mocker.GetMock<IAppTaskQueue>().Setup(x => x.EnqueueAndWaitForCompletion(install, It.IsAny<CancellationToken>()))
			.Callback(() => state = ModInstallState.Installed(isUpdateAvailable: false, isUninstallSupported: true)).Returns(Task.CompletedTask);
		mocker.GetMock<IAppTaskQueue>().Setup(x => x.EnqueueAndWaitForCompletion(uninstall, It.IsAny<CancellationToken>()))
			.Callback(() => state = ModInstallState.NotInstalled).Returns(Task.CompletedTask);
		var sut = mocker.CreateInstance<OptionalModsViewModel>();
		using var activation = await sut.Activator.Activate(CancellationToken.None);
		await TaskAssert.Eventually(() => sut.OptionalMods.Count == 1, "optional mod card");
		var card = sut.OptionalMods.Single();
		await card.InstallCommand.ExecuteAsync(null);
		Assert.That(card.IsInstalled, Is.True);
		await card.UninstallCommand.ExecuteAsync(null);
		Assert.That(card.IsInstalled, Is.False);
		mocker.GetMock<IAppTaskQueue>().Verify(x => x.EnqueueAndWaitForCompletion(install, It.IsAny<CancellationToken>()), Times.Once);
		mocker.GetMock<IAppTaskQueue>().Verify(x => x.EnqueueAndWaitForCompletion(uninstall, It.IsAny<CancellationToken>()), Times.Once);
	}


	private static ModFeedData CreateMod(string name, string supportedVersion) => new()
	{
		Name = name,
		DisplayName = null,
		LogoPath = $"{name.ToLowerInvariant()}.png",
		DescriptionPath = new() { ["ru"] = $"{name.ToLowerInvariant()}.md" },
		Items =
		[
			new ModFeedItem
			{
				Version = NuGetVersion.Parse("1.0.0"),
				ArchiveDownloadInfo = new DownloadInfo { S3Path = $"Mods/{name}.7z" },
				SupportedModpackVersion = VersionRange.Parse(supportedVersion),
			},
		],
	};
}
