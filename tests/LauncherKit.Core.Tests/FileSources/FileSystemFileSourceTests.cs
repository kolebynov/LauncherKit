using LauncherKit.Core.FileSources;
using LauncherKit.Core.Models;
using LauncherKit.Core.Utils;
using LauncherKit.TestSupport;

namespace LauncherKit.Core.Tests.FileSources;

[TestFixture]
public sealed class FileSystemFileSourceTests
{
	[Test]
	public async Task FileOperations_ExistingFile_ReportSizeHashAndResumedContent()
	{
		// Arrange
		using var temp = new TempDirectory();
		var path = temp.CreateFile("source.bin", new byte[] { 1, 2, 3, 4 });
		var expectedHash = new Sha256Hash(new byte[32]);
		var mocker = new AutoMocker(MockBehavior.Strict);
		mocker.Use(path);
		mocker.GetMock<IHashCalculator>()
			.Setup(x => x.HashFile(path, null, CancellationToken.None))
			.ReturnsAsync(expectedHash);
		var sut = mocker.CreateInstance<FileSystemFileSource>();

		// Act
		var size = await sut.GetFileSize(CancellationToken.None);
		var hash = await sut.GetHash(CancellationToken.None);
		await using var stream = await sut.OpenStream(2, null, CancellationToken.None);
		var buffer = new byte[2];
		var count = await stream.Read(buffer, CancellationToken.None);

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(size, Is.EqualTo(4));
			Assert.That(hash, Is.EqualTo(expectedHash));
			Assert.That(count, Is.EqualTo(2));
			Assert.That(buffer, Is.EqualTo(new byte[] { 3, 4 }));
		});
	}
}
