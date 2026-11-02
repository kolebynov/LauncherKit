using System.Diagnostics;
using LauncherKit.Core.Services;
using LauncherKit.Core.Services.Abstractions;
using LauncherKit.Infrastructure;
using LauncherKit.LogsCollector.Core;
using LauncherKit.TestSupport;

namespace LauncherKit.Core.Tests.Services;

[TestFixture]
public sealed class AppLogsCollectorTests
{
	[Test]
	public async Task CollectLogs_SettingsChange_UsesCurrentModOrganizerDirectoryOnEveryCall()
	{
		var mocker = LauncherKit.TestSupport.ProductTestSupport.CreateMocker(MockBehavior.Strict);
		var monitor = OptionsStubs.Monitor(mocker, new AppSettings());
		var path = Path.Combine(Path.GetTempPath(), "stb-log-test");
		mocker.GetMock<ITempStorageService>()
			.Setup(x => x.CreateTempDirectory())
			.Returns(new TempStorageService.TempDirectoryHandle(path, mocker.Get<IFileSystemUtils>()));
		var directories = new List<string?>();
		mocker.GetMock<ILogsCollector>()
			.Setup(x => x.CollectLogs(path, It.IsAny<string?>(), CancellationToken.None))
			.Callback<string, string?, CancellationToken>((_, directory, _) => directories.Add(directory))
			.ReturnsAsync((Exception?)null);
		mocker.GetMock<IProcessRunner>()
			.Setup(x => x.StartProcess(It.IsAny<ProcessStartInfo>(), CancellationToken.None, null))
			.ReturnsAsync(0);
		var sut = mocker.CreateInstance<AppLogsCollector>();

		await sut.CollectLogs(CancellationToken.None);
		var configured = new AppSettings { SkyrimDirectory = Path.Combine(Path.GetTempPath(), "configured-skyrim") };
		monitor.Change(configured);
		await sut.CollectLogs(CancellationToken.None);

		Assert.That(directories, Is.EqualTo(new[] { null, configured.GetModOrganizerDirectory(ProductTestSupport.CreateProduct()) }));
	}

	[Test]
	public async Task CollectLogs_Success_OpensCollectedArchiveDirectory()
	{
		// Arrange
		var mocker = LauncherKit.TestSupport.ProductTestSupport.CreateMocker(MockBehavior.Strict);
		OptionsStubs.Monitor(mocker, new AppSettings());
		var path = Path.Combine(Path.GetTempPath(), "stb-log-test");
		var fileSystem = mocker.GetMock<IFileSystemUtils>();
		mocker.GetMock<ITempStorageService>()
			.Setup(x => x.CreateTempDirectory())
			.Returns(new TempStorageService.TempDirectoryHandle(path, fileSystem.Object));
		mocker.GetMock<ILogsCollector>()
			.Setup(x => x.CollectLogs(path, null, CancellationToken.None))
			.ReturnsAsync((Exception?)null);
		mocker.GetMock<IProcessRunner>()
			.Setup(x => x.StartProcess(
				It.Is<ProcessStartInfo>(info => info.FileName == path && info.UseShellExecute),
				CancellationToken.None,
				null))
			.ReturnsAsync(0);
		var sut = mocker.CreateInstance<AppLogsCollector>();

		// Act
		await sut.CollectLogs(CancellationToken.None);

		// Assert
		mocker.GetMock<IProcessRunner>().VerifyAll();
	}

	[Test]
	public void CollectLogs_CollectorThrows_DisposesTemporaryDirectory()
	{
		// Arrange
		var mocker = LauncherKit.TestSupport.ProductTestSupport.CreateMocker(MockBehavior.Strict);
		OptionsStubs.Monitor(mocker, new AppSettings());
		var path = Path.Combine(Path.GetTempPath(), "stb-log-test");
		var fileSystem = mocker.GetMock<IFileSystemUtils>();
		fileSystem.Setup(x => x.SafeDeleteDirectory(path)).Returns(FileSystemUtils.SafeDeleteResult.Success.Instance);
		mocker.GetMock<ITempStorageService>()
			.Setup(x => x.CreateTempDirectory())
			.Returns(new TempStorageService.TempDirectoryHandle(path, fileSystem.Object));
		mocker.GetMock<ILogsCollector>()
			.Setup(x => x.CollectLogs(path, null, CancellationToken.None))
			.ThrowsAsync(new IOException("archive failed"));
		var sut = mocker.CreateInstance<AppLogsCollector>();

		// Act
		// Assert
		Assert.That(async () => await sut.CollectLogs(CancellationToken.None), Throws.InstanceOf<IOException>());
		fileSystem.Verify(x => x.SafeDeleteDirectory(path), Times.Once);
	}
}
