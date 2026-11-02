using R3;
using LauncherKit.Core.AppTasks.MicroTasks;
using LauncherKit.Core.FileStorage;
using LauncherKit.Core.Models;
using LauncherKit.Core.Services.Abstractions;

namespace LauncherKit.Core.Tests.AppTasks.MicroTasks;

[TestFixture]
public sealed class ExtractSevenZipMicroTaskTests
{
	private const string SkyrimDirectory = @"C:\Skyrim";
	private const string ArchivePath = @"C:\Downloads\archive.7z";

	[Test]
	public async Task Execute_StoredArchive_ExtractsIntoResolvedSkyrimDirectory()
	{
		// Arrange
		var mocker = new AutoMocker(MockBehavior.Strict);
		var destination = new SkyrimRelativePath("Data");
		SetupStoredArchive(mocker);
		mocker.GetMock<ISevenZipService>()
			.Setup(x => x.ExtractArchive(ArchivePath, destination.ToAbsolute(SkyrimDirectory), CancellationToken.None))
			.Returns(Task.CompletedTask);
		var sut = new ExtractSevenZipMicroTask(mocker.GetMock<IFileStorageService>().Object,
			mocker.GetMock<ISevenZipService>().Object, null, "archive.7z", destination, SkyrimDirectory);
		using var subject = new Subject<MicroTaskStep>();

		// Act
		await sut.Execute(subject, CancellationToken.None);

		// Assert
		mocker.GetMock<ISevenZipService>().VerifyAll();
	}

	[Test]
	public async Task Execute_ExcludedEntry_ExtractsArchiveWithoutIt()
	{
		// Arrange
		var mocker = new AutoMocker(MockBehavior.Strict);
		SetupStoredArchive(mocker);
		mocker.GetMock<ISevenZipService>()
			.Setup(x => x.ExtractArchiveExcept(ArchivePath, SkyrimDirectory, @"Data\version.txt", CancellationToken.None))
			.Returns(Task.CompletedTask);
		var sut = new ExtractSevenZipMicroTask(mocker.GetMock<IFileStorageService>().Object,
			mocker.GetMock<ISevenZipService>().Object, new MicroTaskDescription.ExtractArchive("archive.7z"), "archive.7z", SkyrimRelativePath.Root, SkyrimDirectory,
			@"Data\version.txt");
		using var subject = new Subject<MicroTaskStep>();

		// Act
		await sut.Execute(subject, CancellationToken.None);

		// Assert
		mocker.GetMock<ISevenZipService>().VerifyAll();
	}

	[TestCase(true)]
	[TestCase(false)]
	public async Task ExecuteEntry_ArchiveWithOrWithoutEntry_ExtractsOnlyThatEntryWhenPresent(bool containsEntry)
	{
		// Arrange
		var mocker = new AutoMocker(MockBehavior.Strict);
		SetupStoredArchive(mocker);
		var sevenZip = mocker.GetMock<ISevenZipService>();
		sevenZip.Setup(x => x.ContainsEntry(ArchivePath, @"Data\version.txt", CancellationToken.None))
			.ReturnsAsync(containsEntry);
		sevenZip.Setup(x => x.ExtractEntriesFromArchive(
				It.Is<IEnumerable<string>>(paths => paths.SequenceEqual(new[] { @"Data\version.txt" })),
				ArchivePath, SkyrimDirectory, true, CancellationToken.None))
			.Returns(Task.CompletedTask);
		var sut = new ExtractSevenZipEntryMicroTask(mocker.GetMock<IFileStorageService>().Object, sevenZip.Object,
			new MicroTaskDescription.UpdateModpackVersion(), "archive.7z", SkyrimRelativePath.Root, SkyrimDirectory, @"Data\version.txt");
		using var subject = new Subject<MicroTaskStep>();

		// Act
		await sut.Execute(subject, CancellationToken.None);

		// Assert
		sevenZip.Verify(x => x.ExtractEntriesFromArchive(It.IsAny<IEnumerable<string>>(), ArchivePath, SkyrimDirectory, true,
			CancellationToken.None), containsEntry ? Times.Once() : Times.Never());
	}

	private static void SetupStoredArchive(AutoMocker mocker) =>
		mocker.GetMock<IFileStorageService>().Setup(x => x.GetFilePath("archive.7z")).Returns(ArchivePath);
}
