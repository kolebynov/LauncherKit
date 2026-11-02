using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using LauncherKit.Core.Exceptions;
using LauncherKit.Core.Feed;
using LauncherKit.Core.FileSources;
using LauncherKit.Core.Services;
using LauncherKit.Core.Services.Abstractions;
using LauncherKit.Core.Tests.TestSupport;

namespace LauncherKit.Core.Tests.Services;

[TestFixture]
public sealed class FeedAvailabilityCheckerTests
{
	[Test]
	public async Task CheckFeed_ServerServesTestFile_ReturnsTrue()
	{
		// Arrange
		var mocker = CreateMocker();
		DownloadServerFallbackStubs.SingleServer<bool>(mocker, DownloadServer.Proxy);
		var source = mocker.GetMock<IFileSource>();
		mocker.GetMock<IFeedFileSourceFactory>()
			.Setup(x => x.Create(It.Is<DownloadInfo>(info => info.S3Path == "Test.zip"), DownloadServer.Proxy))
			.Returns(source.Object);
		source.Setup(x => x.OpenStream(null, null, It.IsAny<CancellationToken>()))
			.ReturnsAsync(new MemoryFileStream([1, 2, 3]));
		var sut = mocker.CreateInstance<FeedAvailabilityChecker>();

		// Act
		var result = await sut.CheckFeed();

		// Assert
		Assert.That(result, Is.True);
	}

	[Test]
	public async Task CheckFeed_BothServersUnavailable_ReturnsFalse()
	{
		// Arrange
		var mocker = CreateMocker();
		mocker.GetMock<IDownloadServerFallback>()
			.Setup(x => x.Execute(It.IsAny<Func<DownloadServer, CancellationToken, Task<bool>>>(), It.IsAny<CancellationToken>()))
			.ThrowsAsync(new DownloadServersUnavailableException(
				new HttpRequestException("main offline"), new HttpRequestException("proxy offline")));
		var sut = mocker.CreateInstance<FeedAvailabilityChecker>();

		// Act
		var result = await sut.CheckFeed();

		// Assert
		Assert.That(result, Is.False);
	}

	private static AutoMocker CreateMocker()
	{
		var mocker = new AutoMocker(MockBehavior.Strict);
		mocker.Use<ILogger<FeedAvailabilityChecker>>(NullLogger<FeedAvailabilityChecker>.Instance);
		mocker.Use(TimeProvider.System);
		return mocker;
	}

	private sealed class MemoryFileStream(byte[] content) : IFileStream
	{
		private readonly MemoryStream _stream = new(content);

		public long Position => _stream.Position;

		public long FileSize => _stream.Length;

		public ValueTask<int> Read(Memory<byte> buffer, CancellationToken cancellationToken) =>
			_stream.ReadAsync(buffer, cancellationToken);

		public ValueTask DisposeAsync() => _stream.DisposeAsync();
	}
}
