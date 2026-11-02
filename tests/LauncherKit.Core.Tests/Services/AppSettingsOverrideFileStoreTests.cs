using LauncherKit.Core.Services;
using LauncherKit.Infrastructure;
using LauncherKit.TestSupport;

namespace LauncherKit.Core.Tests.Services;

[TestFixture]
public sealed class AppSettingsOverrideFileStoreTests
{
	private const string FilePath = "override.json";

	[Test]
	public async Task Read_MissingFile_ReturnsNull()
	{
		// Arrange
		using var temp = new TempDirectory();
		var mocker = new AutoMocker(MockBehavior.Strict);
		using var sut = CreateSut(mocker, temp.Combine(FilePath));

		// Act
		var result = await sut.Read(CancellationToken.None);

		// Assert
		Assert.That(result, Is.Null);
	}

	[Test]
	public async Task Write_Json_WritesTemporaryFileThenReplacesSettingsFile()
	{
		// Arrange
		var mocker = new AutoMocker(MockBehavior.Strict);
		var fileSystemUtils = mocker.GetMock<IFileSystemUtils>();
		var events = new List<string>();
		string? writtenJson = null;
		fileSystemUtils
			.Setup(x => x.WriteAllText(It.IsAny<string>(), It.IsAny<string>(), CancellationToken.None))
			.Callback<string, string, CancellationToken>((path, json, _) =>
			{
				events.Add($"write {path}");
				writtenJson = json;
			})
			.Returns(Task.CompletedTask);
		fileSystemUtils
			.Setup(x => x.MoveFile(It.IsAny<string>(), FilePath, true))
			.Callback<string, string, bool>((source, _, _) => events.Add($"move {source}"));
		using var sut = CreateSut(mocker, FilePath);

		// Act
		await sut.Write("{\"new\":true}", CancellationToken.None);

		// Assert
		var tempPath = events[0]["write ".Length..];
		Assert.Multiple(() =>
		{
			Assert.That(tempPath, Does.StartWith(FilePath + ".").And.EndsWith(".tmp"));
			Assert.That(events, Is.EqualTo(new[] { $"write {tempPath}", $"move {tempPath}" }));
			Assert.That(writtenJson, Is.EqualTo("{\"new\":true}"));
		});
	}

	[Test]
	public async Task Write_OverlappingWrites_SecondStartsOnlyAfterFirstReplacedFile()
	{
		// Arrange
		var mocker = new AutoMocker(MockBehavior.Strict);
		var fileSystemUtils = mocker.GetMock<IFileSystemUtils>();
		var events = new List<string>();
		var firstWrite = new TaskCompletionSource();
		fileSystemUtils
			.Setup(x => x.WriteAllText(It.IsAny<string>(), It.IsAny<string>(), CancellationToken.None))
			.Returns<string, string, CancellationToken>((path, json, _) =>
			{
				events.Add($"write {json} {path}");
				return json == "first" ? firstWrite.Task : Task.CompletedTask;
			});
		fileSystemUtils
			.Setup(x => x.MoveFile(It.IsAny<string>(), FilePath, true))
			.Callback<string, string, bool>((source, _, _) => events.Add($"move {source}"));
		using var sut = CreateSut(mocker, FilePath);

		// Act
		var first = sut.Write("first", CancellationToken.None);
		var second = sut.Write("second", CancellationToken.None);
		var eventsWhileFirstWrites = events.ToList();
		firstWrite.SetResult();
		await Task.WhenAll(first, second);

		// Assert
		var firstTemp = events[0]["write first ".Length..];
		var secondTemp = events[2]["write second ".Length..];
		Assert.Multiple(() =>
		{
			Assert.That(eventsWhileFirstWrites, Is.EqualTo(new[] { $"write first {firstTemp}" }));
			Assert.That(events, Is.EqualTo(new[]
			{
				$"write first {firstTemp}", $"move {firstTemp}", $"write second {secondTemp}", $"move {secondTemp}",
			}));
			Assert.That(secondTemp, Is.Not.EqualTo(firstTemp));
		});
	}

	[Test]
	public async Task Write_WriteFails_DeletesTemporaryFileRethrowsAndAllowsNextWrite()
	{
		// Arrange
		var mocker = new AutoMocker(MockBehavior.Strict);
		var fileSystemUtils = mocker.GetMock<IFileSystemUtils>();
		var writtenPaths = new List<string>();
		fileSystemUtils
			.Setup(x => x.WriteAllText(It.IsAny<string>(), It.IsAny<string>(), CancellationToken.None))
			.Returns<string, string, CancellationToken>((path, json, _) =>
			{
				writtenPaths.Add(path);
				return json == "failing" ? Task.FromException(new IOException("Disk full")) : Task.CompletedTask;
			});
		fileSystemUtils
			.Setup(x => x.SafeDeleteFile(It.IsAny<string>()))
			.Returns(FileSystemUtils.SafeDeleteResult.Success.Instance);
		fileSystemUtils
			.Setup(x => x.MoveFile(It.IsAny<string>(), FilePath, true));
		using var sut = CreateSut(mocker, FilePath);

		// Act
		var exception = await Assert.ThrowsAsync<IOException>(() => sut.Write("failing", CancellationToken.None));
		await sut.Write("next", CancellationToken.None);

		// Assert
		Assert.That(exception?.Message, Is.EqualTo("Disk full"));
		fileSystemUtils.Verify(x => x.SafeDeleteFile(writtenPaths[0]), Times.Once);
		fileSystemUtils.Verify(x => x.MoveFile(It.IsAny<string>(), FilePath, true), Times.Once);
		fileSystemUtils.Verify(x => x.MoveFile(writtenPaths[1], FilePath, true), Times.Once);
	}

	private static AppSettingsOverrideFileStore CreateSut(AutoMocker mocker, string filePath)
	{
		mocker.Use(filePath);
		return mocker.CreateInstance<AppSettingsOverrideFileStore>(enablePrivate: true);
	}
}
