using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using LauncherKit.TestSupport;

namespace LauncherKit.Infrastructure.Tests;

[TestFixture]
public sealed class FileSystemUtilsTests
{
	[Test]
	public void SafeDeleteDirectory_PopulatedDirectory_RemovesTreeAndReturnsSuccess()
	{
		// Arrange
		using var temp = new TempDirectory();
		var target = temp.CreateDirectory("target");
		temp.CreateFile(Path.Combine("target", "root.txt"), "root");
		temp.CreateFile(Path.Combine("target", "nested", "deep", "leaf.txt"), "leaf");
		var sut = CreateSut();

		// Act
		var result = sut.SafeDeleteDirectory(target);

		// Assert
		Assert.That(result is FileSystemUtils.SafeDeleteResult.Success, Is.True);
		Assert.That(Directory.Exists(target), Is.False);
	}

	[Test]
	public void SafeDeleteDirectory_MissingDirectory_ReturnsNotExist()
	{
		// Arrange
		using var temp = new TempDirectory();
		var missing = temp.Combine("absent");
		var sut = CreateSut();

		// Act
		var result = sut.SafeDeleteDirectory(missing);

		// Assert
		Assert.That(result is FileSystemUtils.SafeDeleteResult.NotExist, Is.True);
	}

	[Test]
	public void SafeDeleteFile_ExistingFile_RemovesFileAndReturnsSuccess()
	{
		// Arrange
		using var temp = new TempDirectory();
		var file = temp.CreateFile("data.txt", "payload");
		var sut = CreateSut();

		// Act
		var result = sut.SafeDeleteFile(file);

		// Assert
		Assert.That(result is FileSystemUtils.SafeDeleteResult.Success, Is.True);
		Assert.That(File.Exists(file), Is.False);
	}

	[Test]
	public void SafeDeleteFile_MissingFile_ReturnsNotExist()
	{
		// Arrange
		using var temp = new TempDirectory();
		var missing = temp.Combine("absent.txt");
		var sut = CreateSut();

		// Act
		var result = sut.SafeDeleteFile(missing);

		// Assert
		Assert.That(result is FileSystemUtils.SafeDeleteResult.NotExist, Is.True);
	}

	[Test]
	public void SafeDeleteFile_DirectoryPath_ReturnsNotExist()
	{
		// Arrange
		using var temp = new TempDirectory();
		var directory = temp.CreateDirectory("folder");
		var sut = CreateSut();

		// Act
		var result = sut.SafeDeleteFile(directory);

		// Assert
		Assert.That(result is FileSystemUtils.SafeDeleteResult.NotExist, Is.True);
		Assert.That(Directory.Exists(directory), Is.True);
	}

	[Test]
	public void MoveContents_MultipleSourceDirectories_MergesFilesPreservingNestedStructure()
	{
		// Arrange
		using var temp = new TempDirectory();
		var firstSource = temp.CreateDirectory("first");
		var secondSource = temp.CreateDirectory("second");
		var movedFromFirst = temp.CreateFile(Path.Combine("first", "a.txt"), "a");
		temp.CreateFile(Path.Combine("first", "sub", "nested.txt"), "nested");
		temp.CreateFile(Path.Combine("second", "b.txt"), "b");
		temp.CreateFile(Path.Combine("second", "sub", "other", "deep.txt"), "deep");
		var destination = temp.Combine("destination");
		var sut = CreateSut();

		// Act
		sut.MoveContents([firstSource, secondSource], destination);

		// Assert
		Assert.That(File.ReadAllText(Path.Combine(destination, "a.txt")), Is.EqualTo("a"));
		Assert.That(File.ReadAllText(Path.Combine(destination, "b.txt")), Is.EqualTo("b"));
		Assert.That(File.ReadAllText(Path.Combine(destination, "sub", "nested.txt")), Is.EqualTo("nested"));
		Assert.That(File.ReadAllText(Path.Combine(destination, "sub", "other", "deep.txt")), Is.EqualTo("deep"));
		Assert.That(File.Exists(movedFromFirst), Is.False);
	}

	[Test]
	public void MoveContents_ConflictingRelativePath_LastSourceWins()
	{
		// Arrange
		using var temp = new TempDirectory();
		var firstSource = temp.CreateDirectory("first");
		var secondSource = temp.CreateDirectory("second");
		temp.CreateFile(Path.Combine("first", "sub", "same.txt"), "from-first");
		temp.CreateFile(Path.Combine("second", "sub", "same.txt"), "from-second");
		var destination = temp.Combine("destination");
		var sut = CreateSut();

		// Act
		sut.MoveContents([firstSource, secondSource], destination);

		// Assert
		Assert.That(File.ReadAllText(Path.Combine(destination, "sub", "same.txt")), Is.EqualTo("from-second"));
	}

	[Test]
	public void MoveContents_MissingSourceDirectory_SkipsItAndStillCreatesDestination()
	{
		// Arrange
		using var temp = new TempDirectory();
		var existingSource = temp.CreateDirectory("present");
		temp.CreateFile(Path.Combine("present", "kept.txt"), "kept");
		var destination = temp.Combine("destination");
		var sut = CreateSut();

		// Act
		sut.MoveContents([temp.Combine("absent"), existingSource], destination);

		// Assert
		Assert.That(File.ReadAllText(Path.Combine(destination, "kept.txt")), Is.EqualTo("kept"));
	}

	[Test]
	public async Task ModifyFileContent_Modifier_WritesModifierResultToFile()
	{
		// Arrange
		using var temp = new TempDirectory();
		var file = temp.CreateFile("content.txt", "original");
		var sut = CreateSut();

		// Act
		await sut.ModifyFileContent(file, content => content + "-modified", CancellationToken.None);

		// Assert
		Assert.That(await File.ReadAllTextAsync(file, CancellationToken.None), Is.EqualTo("original-modified"));
	}

	[Test]
	public async Task ModifyFileLines_Modifier_WritesModifierLinesToFile()
	{
		// Arrange
		using var temp = new TempDirectory();
		var file = temp.CreateFile("lines.txt", string.Join(Environment.NewLine, "one", "two", "three"));
		var sut = CreateSut();

		// Act
		await sut.ModifyFileLines(
			file,
			lines => lines.Where(line => line != "two").Select(line => line.ToUpperInvariant()),
			CancellationToken.None);

		// Assert
		Assert.That(await File.ReadAllLinesAsync(file, CancellationToken.None), Is.EqualTo(new[] { "ONE", "THREE" }));
	}

	[Test]
	public void GetFileSize_ExistingFile_ReturnsByteLength()
	{
		// Arrange
		using var temp = new TempDirectory();
		var file = temp.CreateFile("sized.bin", new byte[] { 1, 2, 3, 4, 5 });
		var sut = CreateSut();

		// Act
		var size = sut.GetFileSize(file);

		// Assert
		Assert.That(size, Is.EqualTo(5L));
	}

	[Test]
	public void GetFileSize_MissingFile_ReturnsNull()
	{
		// Arrange
		using var temp = new TempDirectory();
		var missing = temp.Combine("absent.bin");
		var sut = CreateSut();

		// Act
		var size = sut.GetFileSize(missing);

		// Assert
		Assert.That(size, Is.Null);
	}

	[Test]
	public void EnsureDirectoryExists_MissingDirectory_CreatesFullPath()
	{
		// Arrange
		using var temp = new TempDirectory();
		var directory = temp.Combine(Path.Combine("outer", "inner"));
		var sut = CreateSut();

		// Act
		sut.EnsureDirectoryExists(directory);

		// Assert
		Assert.That(Directory.Exists(directory), Is.True);
	}

	[Test]
	public void EnsureDirectoryExists_ExistingDirectory_KeepsContentIntact()
	{
		// Arrange
		using var temp = new TempDirectory();
		var directory = temp.CreateDirectory("existing");
		var file = temp.CreateFile(Path.Combine("existing", "keep.txt"), "keep");
		var sut = CreateSut();

		// Act
		sut.EnsureDirectoryExists(directory);

		// Assert
		Assert.That(Directory.Exists(directory), Is.True);
		Assert.That(File.ReadAllText(file), Is.EqualTo("keep"));
	}

	[Test]
	public async Task ModifyFileContent_ModifierThrows_LeavesOriginalContentIntact()
	{
		// Arrange
		using var temp = new TempDirectory();
		var file = temp.CreateFile("content.txt", "original");
		var sut = CreateSut();

		// Act
		Assert.That(async () => await sut.ModifyFileContent(
			file,
			_ => throw new InvalidOperationException("modifier failed"),
			CancellationToken.None), Throws.InvalidOperationException);

		// Assert
		Assert.That(await File.ReadAllTextAsync(file, CancellationToken.None), Is.EqualTo("original"));
	}

	[Test]
	public async Task ModifyFileLines_ModifierThrows_LeavesOriginalLinesIntact()
	{
		// Arrange
		using var temp = new TempDirectory();
		var file = temp.CreateFile("lines.txt", "one" + Environment.NewLine + "two");
		var sut = CreateSut();

		// Act
		Assert.That(async () => await sut.ModifyFileLines(
			file,
			_ => throw new InvalidOperationException("modifier failed"),
			CancellationToken.None), Throws.InvalidOperationException);

		// Assert
		Assert.That(await File.ReadAllLinesAsync(file, CancellationToken.None), Is.EqualTo(new[] { "one", "two" }));
	}

	[Test]
	public void GetFullPath_RelativePath_ResolvesAgainstProvidedBase()
	{
		// Arrange
		using var temp = new TempDirectory();
		var sut = CreateSut();

		// Act
		var result = sut.GetFullPath(Path.Combine("nested", "file.txt"), temp.Path);

		// Assert
		Assert.That(result, Is.EqualTo(temp.Combine(Path.Combine("nested", "file.txt"))));
	}

	private static FileSystemUtils CreateSut()
	{
		var mocker = new AutoMocker(MockBehavior.Strict);
		mocker.Use<ILogger<FileSystemUtils>>(NullLogger<FileSystemUtils>.Instance);
		return mocker.CreateInstance<FileSystemUtils>();
	}
}
