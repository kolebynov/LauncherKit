using LauncherKit.Core.FileSources;

namespace LauncherKit.Core.Tests.FileSources;

[TestFixture]
public sealed class ThrottledFileStreamTests
{
	[Test]
	public async Task Read_LimiterGrantsLessThanRequested_ReadsOnlyTheGrantedSlice()
	{
		// Arrange
		var mocker = new AutoMocker(MockBehavior.Strict);
		var requestedLengths = new List<int>();
		mocker.GetMock<IDownloadRateLimiter>()
			.Setup(x => x.AcquireAtMost(16, It.IsAny<CancellationToken>()))
			.Returns(() => ValueTask.FromResult(4));
		mocker.GetMock<IDownloadRateLimiter>().Setup(x => x.Refund(0));
		mocker.GetMock<IFileStream>()
			.Setup(x => x.Read(It.IsAny<Memory<byte>>(), It.IsAny<CancellationToken>()))
			.Returns((Memory<byte> buffer, CancellationToken _) =>
			{
				requestedLengths.Add(buffer.Length);
				return ValueTask.FromResult(buffer.Length);
			});
		var sut = mocker.CreateInstance<ThrottledFileStream>(enablePrivate: true);

		// Act
		var read = await sut.Read(new byte[16], CancellationToken.None);

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(read, Is.EqualTo(4));
			Assert.That(requestedLengths, Is.EqualTo(new[] { 4 }));
		});
		mocker.GetMock<IDownloadRateLimiter>().Verify(x => x.Refund(0), Times.Once);
	}

	[Test]
	public async Task Read_InnerStreamReturnsFewerBytesThanGranted_RefundsTheRemainder()
	{
		// Arrange
		var mocker = new AutoMocker(MockBehavior.Strict);
		mocker.GetMock<IDownloadRateLimiter>()
			.Setup(x => x.AcquireAtMost(16, It.IsAny<CancellationToken>()))
			.Returns(() => ValueTask.FromResult(4));
		mocker.GetMock<IDownloadRateLimiter>().Setup(x => x.Refund(1));
		mocker.GetMock<IFileStream>()
			.Setup(x => x.Read(It.IsAny<Memory<byte>>(), It.IsAny<CancellationToken>()))
			.Returns(() => ValueTask.FromResult(3));
		var sut = mocker.CreateInstance<ThrottledFileStream>(enablePrivate: true);

		// Act
		var read = await sut.Read(new byte[16], CancellationToken.None);

		// Assert
		Assert.That(read, Is.EqualTo(3));
		mocker.GetMock<IDownloadRateLimiter>().Verify(x => x.Refund(1), Times.Once);
	}

	[Test]
	public async Task Read_InnerStreamThrows_RefundsTheWholeGrant()
	{
		// Arrange
		var mocker = new AutoMocker(MockBehavior.Strict);
		mocker.GetMock<IDownloadRateLimiter>()
			.Setup(x => x.AcquireAtMost(16, It.IsAny<CancellationToken>()))
			.Returns(() => ValueTask.FromResult(4));
		mocker.GetMock<IDownloadRateLimiter>().Setup(x => x.Refund(4));
		mocker.GetMock<IFileStream>()
			.Setup(x => x.Read(It.IsAny<Memory<byte>>(), It.IsAny<CancellationToken>()))
			.Throws(new IOException("boom"));
		var sut = mocker.CreateInstance<ThrottledFileStream>(enablePrivate: true);

		// Act
		// Assert
		await Assert.ThrowsAsync<IOException>(async () => await sut.Read(new byte[16], CancellationToken.None));
		mocker.GetMock<IDownloadRateLimiter>().Verify(x => x.Refund(4), Times.Once);
	}

	[Test]
	public void Position_InnerStreamAdvanced_ReturnsInnerValue()
	{
		// Arrange
		var mocker = new AutoMocker(MockBehavior.Strict);
		mocker.GetMock<IFileStream>().Setup(x => x.Position).Returns(4096);
		var sut = mocker.CreateInstance<ThrottledFileStream>(enablePrivate: true);

		// Act
		var position = sut.Position;

		// Assert
		Assert.That(position, Is.EqualTo(4096));
	}

	[Test]
	public async Task Read_EmptyBuffer_DoesNotAcquireFromTheLimiter()
	{
		// Arrange
		var mocker = new AutoMocker(MockBehavior.Strict);
		mocker.GetMock<IFileStream>()
			.Setup(x => x.Read(It.IsAny<Memory<byte>>(), It.IsAny<CancellationToken>()))
			.Returns(() => ValueTask.FromResult(0));
		var sut = mocker.CreateInstance<ThrottledFileStream>(enablePrivate: true);

		// Act
		var read = await sut.Read(Memory<byte>.Empty, CancellationToken.None);

		// Assert
		Assert.That(read, Is.Zero);
		mocker.GetMock<IDownloadRateLimiter>()
			.Verify(x => x.AcquireAtMost(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
	}
}
