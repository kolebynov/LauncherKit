using System.Formats.Tar;
using System.IO.Compression;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NuGet.Versioning;
using LauncherKit.Infrastructure;
using LauncherKit.TestSupport;

namespace LauncherKit.Bootstrapper.Tests;

[TestFixture]
public sealed class LauncherInstallerTests
{
	[Test]
	public void ExtractLauncher_BrotliTar_WritesArchiveFiles()
	{
		// Arrange
		using var temp = new TempDirectory();
		var destination = temp.Combine("launcher");
		Directory.CreateDirectory(destination);
		using var archive = CreateArchive("app.bin", "payload");
		var mocker = CreateMocker();
		mocker.GetMock<IFileSystemUtils>().Setup(x => x.EnsureDirectoryExists(destination));
		var sut = mocker.CreateInstance<LauncherInstaller>(enablePrivate: true);

		// Act
		sut.ExtractLauncher(archive, destination);

		// Assert
		Assert.That(File.ReadAllText(Path.Combine(destination, "app.bin")), Is.EqualTo("payload"));
	}

	[Test]
	public void MigrateUserSettings_PreviousSettingsExist_CopiesWithOverwrite()
	{
		// Arrange
		using var temp = new TempDirectory();
		var previous = temp.Combine("previous");
		var current = temp.Combine("current");
		Directory.CreateDirectory(previous);
		Directory.CreateDirectory(current);
		File.WriteAllText(Path.Combine(previous, "consumer.settings.json"), "old-settings");
		File.WriteAllText(Path.Combine(current, "consumer.settings.json"), "replace-me");
		var sut = CreateMocker().CreateInstance<LauncherInstaller>(enablePrivate: true);

		// Act
		sut.MigrateUserSettings(previous, current);

		// Assert
		Assert.That(File.ReadAllText(Path.Combine(current, "consumer.settings.json")), Is.EqualTo("old-settings"));
	}

	[Test]
	public async Task RemovePrevVersions_VersionDirectories_KeepsCurrentAndDeletesOthers()
	{
		// Arrange
		using var temp = new TempDirectory();
		var oldDirectory = temp.Combine("1.0.0");
		var currentDirectory = temp.Combine("2.0.0");
		Directory.CreateDirectory(oldDirectory);
		Directory.CreateDirectory(currentDirectory);
		var mocker = CreateMocker();
		mocker.GetMock<IFileSystemUtils>()
			.Setup(x => x.SafeDeleteDirectory(oldDirectory))
			.Returns(FileSystemUtils.SafeDeleteResult.Success.Instance);
		var sut = mocker.CreateInstance<LauncherInstaller>(enablePrivate: true);

		// Act
		await sut.RemovePrevVersions(temp.Path, new NuGetVersion(2, 0, 0), CancellationToken.None);

		// Assert
		mocker.GetMock<IFileSystemUtils>().Verify(x => x.SafeDeleteDirectory(oldDirectory), Times.Once);
	}

	private static AutoMocker CreateMocker()
	{
		var mocker = LauncherKit.TestSupport.ProductTestSupport.CreateMocker(MockBehavior.Strict);
		mocker.Use<TimeProvider>(new FakeTimeProvider());
		mocker.Use<Microsoft.Extensions.Logging.ILogger<LauncherInstaller>>(NullLogger<LauncherInstaller>.Instance);
		mocker.Use<string>("consumer.settings.json");
		return mocker;
	}

	private static MemoryStream CreateArchive(string name, string content)
	{
		var archive = new MemoryStream();
		using (var brotli = new BrotliStream(archive, CompressionLevel.Optimal, leaveOpen: true))
		using (var writer = new TarWriter(brotli, leaveOpen: false))
		{
			writer.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, name)
			{
				DataStream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content)),
			});
		}

		archive.Position = 0;
		return archive;
	}
}
