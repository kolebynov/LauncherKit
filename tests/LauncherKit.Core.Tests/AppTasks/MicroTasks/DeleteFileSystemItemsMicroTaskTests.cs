using R3;
using LauncherKit.Core.AppTasks.MicroTasks;
using LauncherKit.Core.Models;
using LauncherKit.Infrastructure;
using LauncherKit.TestSupport;

namespace LauncherKit.Core.Tests.AppTasks.MicroTasks;

[TestFixture]
public sealed class DeleteFileSystemItemsMicroTaskTests
{
	[Test]
	public async Task Execute_ExistingFile_DeletesAbsoluteSkyrimPath()
	{
		// Arrange
		using var temp = new TempDirectory();
		var relativePath = new SkyrimRelativePath(Path.Combine("Data", "obsolete.bin"));
		var absolutePath = relativePath.ToAbsolute(temp.Path);
		Directory.CreateDirectory(Path.GetDirectoryName(absolutePath)!);
		File.WriteAllText(absolutePath, "obsolete");
		var mocker = new AutoMocker(MockBehavior.Strict);
		mocker.Use(temp.Path);
		mocker.Use<IReadOnlyCollection<SkyrimRelativePath>>([relativePath]);
		mocker.Use(true);
		mocker.Use<MicroTaskDescription>(new MicroTaskDescription.DeleteFiles());
		mocker.GetMock<IFileSystemUtils>()
			.Setup(x => x.SafeDeleteFile(absolutePath))
			.Returns(FileSystemUtils.SafeDeleteResult.Success.Instance);
		var sut = mocker.CreateInstance<DeleteFileSystemItemsMicroTask>(enablePrivate: true);
		using var subject = new Subject<MicroTaskStep>();

		// Act
		await sut.Execute(subject, CancellationToken.None);

		// Assert
		mocker.GetMock<IFileSystemUtils>().Verify(x => x.SafeDeleteFile(absolutePath), Times.Once);
	}

	[Test]
	public void Execute_DeleteFailsAndFailureIsRequired_ThrowsWithPathAndCause()
	{
		// Arrange
		using var temp = new TempDirectory();
		var relativePath = new SkyrimRelativePath("obsolete.bin");
		var absolutePath = relativePath.ToAbsolute(temp.Path);
		File.WriteAllText(absolutePath, "obsolete");
		var cause = new IOException("locked");
		var mocker = new AutoMocker(MockBehavior.Strict);
		mocker.Use(temp.Path);
		mocker.Use<IReadOnlyCollection<SkyrimRelativePath>>([relativePath]);
		mocker.Use(true);
		mocker.Use<MicroTaskDescription>(new MicroTaskDescription.DeleteFiles());
		mocker.GetMock<IFileSystemUtils>()
			.Setup(x => x.SafeDeleteFile(absolutePath))
			.Returns(new FileSystemUtils.SafeDeleteResult.Error(cause));
		var sut = mocker.CreateInstance<DeleteFileSystemItemsMicroTask>(enablePrivate: true);
		using var subject = new Subject<MicroTaskStep>();

		// Act
		// Assert
		Assert.That(async () => await sut.Execute(subject, CancellationToken.None),
			Throws.TypeOf<LauncherKit.Core.Exceptions.FileSystemItemDeletionException>().With.InnerException.SameAs(cause));
	}
}
