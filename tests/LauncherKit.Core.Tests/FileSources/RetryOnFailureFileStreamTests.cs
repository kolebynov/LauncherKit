using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Polly;
using LauncherKit.Core.FileSources;
using LauncherKit.TestSupport;

namespace LauncherKit.Core.Tests.FileSources;

[TestFixture]
public sealed class RetryOnFailureFileStreamTests
{
	[Test]
	public async Task Read_FailsTwice_ResumesFromTheCumulativeOffsetWithoutDuplicatingBytes()
	{
		// Arrange
		var content = Enumerable.Range(0, 300).Select(x => (byte)x).ToArray();
		var rangeStart = 100L;
		var rangeEnd = 199L;
		var openedOffsets = new List<long>();
		var failAfter = new Queue<int>([50, 20]);

		Task<IFileStream> OpenStream(long from)
		{
			openedOffsets.Add(from);
			var data = content.AsSpan((int)from, (int)(rangeEnd - from + 1)).ToArray();
			return Task.FromResult<IFileStream>(
				new FailingFileStream(data, failAfter.Count > 0 ? failAfter.Dequeue() : int.MaxValue));
		}

		await using var sut = new RetryOnFailureFileStream(
			await OpenStream(rangeStart),
			(offset, _) => OpenStream(rangeStart + offset),
			NullLogger<RetryOnFailureFileStream>.Instance,
			Policy<int>.Handle<IOException>(),
			TimeProvider.System);

		// Act
		var received = new List<byte>();
		var buffer = new byte[10];
		int read;
		while ((read = await sut.Read(buffer, CancellationToken.None)) > 0)
		{
			received.AddRange(buffer.AsSpan(0, read).ToArray());
		}

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(openedOffsets, Is.EqualTo(new[] { 100L, 150L, 170L }));
			Assert.That(received, Is.EqualTo(content[100..200]));
			Assert.That(sut.Position, Is.EqualTo(100));
		});
	}

	[Test]
	public async Task Read_CancelledDuringRetryBackoff_StopsRetryWork()
	{
		// Arrange
		using var cancellation = new CancellationTokenSource();
		var mocker = new AutoMocker(MockBehavior.Strict);
		var initial = mocker.GetMock<IFileStream>();
		initial.Setup(x => x.Read(It.IsAny<Memory<byte>>(), It.IsAny<CancellationToken>()))
			.Returns(() =>
			{
				cancellation.Cancel();
				return ValueTask.FromException<int>(new IOException("connection reset"));
			});
		initial.Setup(x => x.DisposeAsync()).Returns(() => ValueTask.CompletedTask);
		var reopened = false;
		mocker.Use<IFileStream>(initial.Object);
		mocker.Use<Func<long, CancellationToken, Task<IFileStream>>>((_, _) =>
		{
			reopened = true;
			return Task.FromResult(initial.Object);
		});
		mocker.Use<Microsoft.Extensions.Logging.ILogger<RetryOnFailureFileStream>>(
			NullLogger<RetryOnFailureFileStream>.Instance);
		mocker.Use<PolicyBuilder<int>>(Policy<int>.Handle<IOException>());
		mocker.Use<TimeProvider>(new FakeTimeProvider());
		await using var sut = mocker.CreateInstance<RetryOnFailureFileStream>(enablePrivate: true);
		var buffer = new byte[8];

		// Act
		var read = sut.Read(buffer, cancellation.Token).AsTask();

		// Assert
		Assert.That(async () => await TaskAssert.WithinTimeout(read), Throws.InstanceOf<OperationCanceledException>());
		Assert.That(reopened, Is.False);
	}

	private sealed class FailingFileStream(byte[] data, int failAfter) : IFileStream
	{
		private int _position;

		public long Position => _position;

		public ValueTask<int> Read(Memory<byte> buffer, CancellationToken cancellationToken)
		{
			if (_position >= failAfter)
			{
				throw new IOException("read failed");
			}

			var count = Math.Min(buffer.Length, data.Length - _position);
			data.AsSpan(_position, count).CopyTo(buffer.Span);
			_position += count;
			return ValueTask.FromResult(count);
		}

		public ValueTask DisposeAsync() => ValueTask.CompletedTask;
	}
}
