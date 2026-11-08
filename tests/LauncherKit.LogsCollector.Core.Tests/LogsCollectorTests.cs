using System.IO.Compression;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using LauncherKit.Infrastructure;
using LauncherKit.TestSupport;

namespace LauncherKit.LogsCollector.Core.Tests;

[TestFixture]
public sealed class LogsCollectorTests
{
	[Test]
	public async Task CollectLogs_LauncherFiles_ArchiveContainsExpectedEntriesAndCollectorLog()
	{
		// Arrange
		using var temp = new TempDirectory();
		var rootLog = temp.CreateFile("root.log", "root");
		temp.CreateDirectory(Path.Combine("1.2.3", "logs"));
		temp.CreateFile(Path.Combine("1.2.3", "appsettings.json"), "{}");
		temp.CreateFile(Path.Combine("1.2.3", "console.log"), "console");
		temp.CreateFile(Path.Combine("1.2.3", "logs", "app.log"), "application");
		var output = temp.Combine("output");
		var sut = CreateSut(temp.Path, temp.Combine("1.2.3"), output, temp.Combine("documents"));

		// Act
		var exception = await sut.CollectLogs(output, null, CancellationToken.None);

		// Assert
		Assert.That(exception, Is.Null);
		using var archive = ZipFile.OpenRead(Path.Combine(output, "collected_logs.zip"));
		Assert.That(archive.Entries.Select(x => x.FullName), Is.EquivalentTo(new[]
		{
			Path.GetFileName(rootLog),
			Path.Combine("1.2.3", "appsettings.json"),
			Path.Combine("1.2.3", "console.log"),
			Path.Combine("1.2.3", "logs", "app.log"),
			"logs-collector.log",
		}));
	}

	[Test]
	public async Task CollectLogs_MissingLogsDirectory_CompletesWithoutException()
	{
		// Arrange
		using var temp = new TempDirectory();
		temp.CreateDirectory("1.2.3");
		var output = temp.Combine("output");
		var sut = CreateSut(temp.Path, temp.Combine("1.2.3"), output, temp.Combine("documents"));

		// Act
		var exception = await sut.CollectLogs(output, null, CancellationToken.None);

		// Assert
		Assert.That(exception, Is.Null);
	}

	[Test]
	public async Task CollectLogs_OutputCannotBeCreated_ReturnsExceptionInsteadOfThrowing()
	{
		// Arrange
		using var temp = new TempDirectory();
		var output = temp.CreateFile("not-a-directory", "content");
		var sut = CreateSut(temp.Path, null, output, temp.Combine("documents"), createOutput: false);

		// Act
		var exception = await sut.CollectLogs(output, null, CancellationToken.None);

		// Assert
		Assert.That(exception, Is.InstanceOf<IOException>());
	}

	[Test]
	public async Task CollectLogs_SkseLogsDirectory_ArchivesLogFilesOnly()
	{
		// Arrange
		using var temp = new TempDirectory();
		using var documents = new TempDirectory();
		var skseDirectory = Path.Combine("My Games", "Skyrim Special Edition", "SKSE");
		documents.CreateFile(Path.Combine(skseDirectory, "crash.log"), "skse");
		documents.CreateFile(Path.Combine(skseDirectory, "crash.dmp"), "dump");
		var output = temp.Combine("output");
		var sut = CreateSut(temp.Path, temp.Combine("1.2.3"), output, documents.Path);

		// Act
		var exception = await sut.CollectLogs(output, null, CancellationToken.None);

		// Assert
		Assert.That(exception, Is.Null);
		using var archive = ZipFile.OpenRead(Path.Combine(output, "collected_logs.zip"));
		var entries = archive.Entries.Select(x => x.FullName).ToList();
		Assert.Multiple(() =>
		{
			Assert.That(entries, Does.Contain(Path.Combine("skse", "crash.log")));
			Assert.That(entries, Has.None.EndsWith(".dmp"));
		});
	}

	[Test]
	public async Task CollectLogs_ModOrganizerLogsDirectory_ArchivesLogsUnderModOrganizerPrefix()
	{
		// Arrange
		using var temp = new TempDirectory();
		using var skyrim = new TempDirectory();
		skyrim.CreateFile(Path.Combine("custom-organizer", "logs", "usvfs.log"), "usvfs");
		var output = temp.Combine("output");
		var sut = CreateSut(temp.Path, temp.Combine("1.2.3"), output, temp.Combine("documents"));

		// Act
		var exception = await sut.CollectLogs(output, skyrim.Combine("custom-organizer"), CancellationToken.None);

		// Assert
		Assert.That(exception, Is.Null);
		using var archive = ZipFile.OpenRead(Path.Combine(output, "collected_logs.zip"));
		Assert.That(archive.Entries.Select(x => x.FullName),
			Does.Contain(Path.Combine("mod-organizer", "usvfs.log")));
	}

	[Test]
	public async Task CollectLogs_ModOrganizerLogsDirectoryMissing_CompletesWithoutModOrganizerEntries()
	{
		// Arrange
		using var temp = new TempDirectory();
		using var skyrim = new TempDirectory();
		var output = temp.Combine("output");
		var sut = CreateSut(temp.Path, temp.Combine("1.2.3"), output, temp.Combine("documents"));

		// Act
		var exception = await sut.CollectLogs(output, skyrim.Combine("missing-organizer"), CancellationToken.None);

		// Assert
		Assert.That(exception, Is.Null);
		using var archive = ZipFile.OpenRead(Path.Combine(output, "collected_logs.zip"));
		Assert.That(archive.Entries.Select(x => x.FullName), Has.None.StartsWith("mod-organizer"));
	}

	[Test]
	public async Task CollectLogs_NoInstalledLauncherVersion_StillArchivesSkseLogs()
	{
		// Arrange
		using var temp = new TempDirectory();
		using var documents = new TempDirectory();
		documents.CreateFile(
			Path.Combine("My Games", "Skyrim Special Edition", "SKSE", "skse64.log"), "skse");
		var output = temp.Combine("output");
		var sut = CreateSut(temp.Path, null, output, documents.Path);

		// Act
		var exception = await sut.CollectLogs(output, null, CancellationToken.None);

		// Assert
		Assert.That(exception, Is.Null);
		using var archive = ZipFile.OpenRead(Path.Combine(output, "collected_logs.zip"));
		Assert.That(archive.Entries.Select(x => x.FullName),
			Does.Contain(Path.Combine("skse", "skse64.log")));
	}

	[Test]
	public async Task CollectLogs_UnknownLaunchersRoot_StillArchivesSkseLogs()
	{
		// Arrange
		using var temp = new TempDirectory();
		using var documents = new TempDirectory();
		documents.CreateFile(
			Path.Combine("My Games", "Skyrim Special Edition", "SKSE", "skse64.log"), "skse");
		var output = temp.Combine("output");
		var sut = CreateSut(null, null, output, documents.Path);

		// Act
		var exception = await sut.CollectLogs(output, null, CancellationToken.None);

		// Assert
		Assert.That(exception, Is.Null);
		using var archive = ZipFile.OpenRead(Path.Combine(output, "collected_logs.zip"));
		Assert.That(archive.Entries.Select(x => x.FullName),
			Is.EquivalentTo(new[] { Path.Combine("skse", "skse64.log"), "logs-collector.log" }));
	}

	[Test]
	public async Task CollectLogs_ConsumerSource_ArchivesActualBytesAndCollectorLog()
	{
		using var temp = new TempDirectory();
		var pluginFile = temp.CreateFile("plugin.txt", "consumer diagnostic");
		var output = temp.Combine("output");
		var mocker = new AutoMocker(MockBehavior.Strict);
		mocker.GetMock<IFileSystemUtils>().Setup(x => x.EnsureDirectoryExists(output, true))
			.Callback(() => Directory.CreateDirectory(output));
		mocker.Use<IEnumerable<ILogsSource>>([new DiagnosticSource(pluginFile)]);
		mocker.Use<ILogger<LogsCollector>>(NullLogger<LogsCollector>.Instance);
		mocker.Use<TimeProvider>(TimeProvider.System);
		var sut = mocker.CreateInstance<LogsCollector>();

		Assert.That(await sut.CollectLogs(output, "consumer-install", CancellationToken.None), Is.Null);
		using var archive = ZipFile.OpenRead(Path.Combine(output, "collected_logs.zip"));
		Assert.That(archive.Entries.Select(x => x.FullName),
			Is.EquivalentTo(new[] { "consumer/diagnostic.txt", "logs-collector.log" }));
		using var reader = new StreamReader(archive.GetEntry("consumer/diagnostic.txt")!.Open());
		Assert.That(await reader.ReadToEndAsync(), Is.EqualTo("consumer diagnostic"));
	}

	private sealed class DiagnosticSource(string file) : ILogsSource
	{
		public IEnumerable<LogFile> GetFiles(string? modOrganizerDirectory, ILogger logger)
		{
			Assert.That(modOrganizerDirectory, Is.EqualTo("consumer-install"));
			yield return new LogFile(file, "consumer/diagnostic.txt");
		}
	}

	// A null launcherRoot is a root the installer has not registered.
	private static LogsCollector CreateSut(
		string? launcherRoot,
		string? installedLauncherDirectory,
		string output,
		string documentsDirectory,
		bool createOutput = true)
	{
		var mocker = new AutoMocker(MockBehavior.Strict);
		mocker.Use<ILogger<LogsCollector>>(NullLogger<LogsCollector>.Instance);
		mocker.Use<TimeProvider>(new FakeTimeProvider(new DateTimeOffset(2026, 9, 8, 12, 0, 0, TimeSpan.Zero)));
		var root = mocker.GetMock<ILaunchersRootDirectoryManager>().SetupGet(x => x.Directory);
		if (launcherRoot == null)
		{
			root.Throws(new InvalidOperationException("Launchers root is not registered."));
		}
		else
		{
			root.Returns(launcherRoot);
		}

		mocker.GetMock<ILaunchersRootDirectoryManager>().Setup(x => x.TryGetInstalledLauncherDirectory()).Returns(installedLauncherDirectory);
		mocker.GetMock<IFileSystemUtils>()
			.Setup(x => x.GetSpecialFolderPath(Environment.SpecialFolder.MyDocuments))
			.Returns(documentsDirectory);
		mocker.GetMock<IFileSystemUtils>()
			.Setup(x => x.EnsureDirectoryExists(output, true))
			.Callback(() =>
			{
				if (createOutput)
				{
					Directory.CreateDirectory(output);
				}
			});
		return new LogsCollector(mocker.Get<IFileSystemUtils>(),
			[new LauncherLogsSource(mocker.Get<ILaunchersRootDirectoryManager>()),
				new SkseLogsSource(mocker.Get<IFileSystemUtils>()), ModOrganizerLogsSource.Instance],
			mocker.Get<ILogger<LogsCollector>>(), mocker.Get<TimeProvider>());
	}
}
