using LauncherKit.Desktop.ViewModels;
using LauncherKit.Core.FileStorage;
using LauncherKit.Infrastructure;

namespace LauncherKit.Desktop.Tests.ViewModels;

[TestFixture]
public sealed class DownloadsViewModelTests
{
	[Test]
	public async Task Activate_StorageFiles_ProjectsFinishedAndPartialDownloadsWithStatus()
	{
		// Arrange
		var mocker = new AutoMocker(MockBehavior.Strict);
		mocker.GetMock<IFileStorageService>().Setup(x => x.GetFileInfos()).Returns([
			CreateFile("finished.zip", 12, StoredFileStatus.Complete),
			CreateFile("partial.zip", 5, StoredFileStatus.Partial),
		]);
		var sut = mocker.CreateInstance<DownloadsViewModel>();

		// Act
		using var activation = await sut.Activator.Activate(CancellationToken.None);

		// Assert
		Assert.That(
			sut.Files.Select(x => (x.FileName, x.Size, x.Status)),
			Is.EqualTo(new[]
			{
				("finished.zip", 12L, StoredFileStatus.Complete),
				("partial.zip", 5L, StoredFileStatus.Partial),
			}));
	}

	[Test]
	public void DeleteSelectedFiles_SelectedItem_DeletesStorageAndRemovesProjection()
	{
		// Arrange
		var mocker = new AutoMocker(MockBehavior.Strict);
		mocker.GetMock<IFileStorageService>()
			.Setup(x => x.Delete("selected.zip"))
			.Returns(IFileStorageService.DeleteResult.Deleted.Instance);
		var sut = mocker.CreateInstance<DownloadsViewModel>();
		var selected = CreateViewModel("selected.zip");
		sut.Files.Add(selected);
		sut.Files.Add(CreateViewModel("kept.zip"));
		sut.SelectedFiles = [selected];

		// Act
		var errors = sut.DeleteSelectedFiles();

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(errors.Single().Result is IFileStorageService.DeleteResult.Deleted, Is.True);
			Assert.That(sut.Files.Select(x => x.FileName), Is.EqualTo(new[] { "kept.zip" }));
		});
	}

	[Test]
	public void DeleteAllFiles_OneFileInUse_KeepsItAndReportsWhy()
	{
		// Arrange
		var mocker = new AutoMocker(MockBehavior.Strict);
		var storage = mocker.GetMock<IFileStorageService>();
		storage.Setup(x => x.Delete("free.zip")).Returns(IFileStorageService.DeleteResult.Deleted.Instance);
		storage.Setup(x => x.Delete("locked.zip"))
			.Returns(new IFileStorageService.DeleteResult.FileInUse("locked.zip.part"));
		var sut = mocker.CreateInstance<DownloadsViewModel>();
		sut.Files.Add(CreateViewModel("free.zip"));
		sut.Files.Add(CreateViewModel("locked.zip"));

		// Act
		var errors = sut.DeleteAllFiles();

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(errors, Has.Count.EqualTo(2));
			Assert.That(errors[1].FileName, Is.EqualTo("locked.zip"));
			Assert.That(errors[1].Result is IFileStorageService.DeleteResult.FileInUse, Is.True);
			Assert.That(sut.Files.Select(x => x.FileName), Is.EqualTo(new[] { "locked.zip" }));
		});
	}

	[Test]
	public void DeleteSelectedFiles_UnrecognizedError_ReportsExceptionMessage()
	{
		// Arrange
		var mocker = new AutoMocker(MockBehavior.Strict);
		mocker.GetMock<IFileStorageService>()
			.Setup(x => x.Delete("broken.zip"))
			.Returns(new IFileStorageService.DeleteResult.Error("broken.zip", new IOException("Disk failure")));
		var sut = mocker.CreateInstance<DownloadsViewModel>();
		var selected = CreateViewModel("broken.zip");
		sut.Files.Add(selected);
		sut.SelectedFiles = [selected];

		// Act
		var errors = sut.DeleteSelectedFiles();

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(errors.Single().FileName, Is.EqualTo("broken.zip"));
			Assert.That(errors.Single().Result is IFileStorageService.DeleteResult.Error, Is.True);
			Assert.That(sut.Files, Has.Count.EqualTo(1));
		});
	}

	[Test]
	public void ColumnComparers_TwoFiles_CompareByTheirColumnValues()
	{
		// Arrange
		var partial = new DownloadsViewModel.FileInfoViewModel
		{
			FileName = "b.zip",
			Size = 1,
			Status = StoredFileStatus.Partial,
		};
		var complete = new DownloadsViewModel.FileInfoViewModel
		{
			FileName = "a.zip",
			Size = 2,
			Status = StoredFileStatus.Complete,
		};

		// Act
		var byName = DownloadsComparers.FileNameComparer.Compare(partial, complete);
		var bySize = DownloadsComparers.SizeComparer.Compare(partial, complete);
		var byStatus = DownloadsComparers.StatusComparer.Compare(partial, complete);

		// Assert
		using (Assert.EnterMultipleScope())
		{
			Assert.That(byName, Is.Positive);
			Assert.That(bySize, Is.Negative);
			Assert.That(byStatus, Is.Positive);
		}
	}

	private static DownloadsViewModel.FileInfoViewModel CreateViewModel(string name) =>
		new() { FileName = name, Size = 10, Status = StoredFileStatus.Complete };

	private static StoredFileInfo CreateFile(string name, long size, StoredFileStatus status) => new()
	{
		FileName = name,
		Size = size,
		Status = status,
		LastWriteAt = DateTimeOffset.UnixEpoch,
	};
}
