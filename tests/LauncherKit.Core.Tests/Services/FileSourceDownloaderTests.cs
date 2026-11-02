using Microsoft.Extensions.Logging.Abstractions;
using LauncherKit.Core.FileSources;
using LauncherKit.Core.FileStorage;
using LauncherKit.Core.Models;
using LauncherKit.Core.Services;
using LauncherKit.Core.Services.Abstractions;
using LauncherKit.Core.Tests.FileStorage;
using LauncherKit.Core.Tests.TestSupport;
using LauncherKit.Core.UserSettings;
using LauncherKit.TestSupport;

namespace LauncherKit.Core.Tests.Services;

[TestFixture]
public sealed class FileSourceDownloaderTests
{
	private static readonly TimeSpan CompletionTimeout = TimeSpan.FromSeconds(30);

	[Test]
	public async Task Download_SinglePartFile_WritesPayloadWithoutWaitingForOtherParts()
	{
		// Arrange
		using var database = new SqliteDatabase();
		using var context = database.CreateMigratedGlobalContext();
		using var temp = new TempDirectory();
		using var storage = FileStorageServiceTests.CreateSut(context, temp.Path, out _);
		var payload = CreatePayload(1024);
		storage.GetOrCreate("payload.bin", payload.Length, null);
		var opened = storage.Allocate("payload.bin", 1);
		var source = new FakeFileSource(payload);
		var sut = CreateSut(out _);

		// Act
		await sut.Download(source, opened, null, CancellationToken.None).WaitAsync(CompletionTimeout);
		await opened.DisposeAsync();

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(File.ReadAllBytes(opened.FullPath), Is.EqualTo(payload));
			Assert.That(source.RequestedRanges, Is.EqualTo(new[] { (0L, (long)payload.Length - 1) }));
		});
	}

	[Test]
	public async Task Download_SourceWithoutRangeSupport_DownloadsWholeFileWithoutRange()
	{
		// Arrange
		using var database = new SqliteDatabase();
		using var context = database.CreateMigratedGlobalContext();
		using var temp = new TempDirectory();
		using var storage = FileStorageServiceTests.CreateSut(context, temp.Path, out _);
		var payload = CreatePayload(1024);
		storage.GetOrCreate("payload.bin", payload.Length, null);
		var opened = storage.Allocate("payload.bin", 1);
		var source = new FakeFileSource(payload, supportsOffsets: false);
		var sut = CreateSut(out _);

		// Act
		await sut.Download(source, opened, null, CancellationToken.None).WaitAsync(CompletionTimeout);
		await opened.DisposeAsync();

		// Assert
		Assert.That(File.ReadAllBytes(opened.FullPath), Is.EqualTo(payload));
	}

	[Test]
	public async Task Download_MultiplePartsFile_WritesEveryPartAtItsOffset()
	{
		// Arrange
		using var database = new SqliteDatabase();
		using var context = database.CreateMigratedGlobalContext();
		using var temp = new TempDirectory();
		using var storage = FileStorageServiceTests.CreateSut(context, temp.Path, out _);
		var payload = CreatePayload(1000);
		storage.GetOrCreate("payload.bin", payload.Length, null);
		var opened = storage.Allocate("payload.bin", 4);
		var source = new FakeFileSource(payload);
		var sut = CreateSut(out _);

		// Act
		await sut.Download(source, opened, null, CancellationToken.None).WaitAsync(CompletionTimeout);
		await opened.DisposeAsync();

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(File.ReadAllBytes(opened.FullPath), Is.EqualTo(payload));
			Assert.That(source.RequestedRanges.OrderBy(x => x.From),
				Is.EqualTo(new[] { (0L, 249L), (250L, 499L), (500L, 749L), (750L, 999L) }));
		});
	}

	[Test]
	public async Task Download_ResumedPart_RequestsOnlyTheRemainderOfThatPart()
	{
		// Arrange
		using var database = new SqliteDatabase();
		using var context = database.CreateMigratedGlobalContext();
		using var temp = new TempDirectory();
		using var storage = FileStorageServiceTests.CreateSut(context, temp.Path, out _);
		var payload = CreatePayload(16 * 1024 * 1024);
		storage.GetOrCreate("payload.bin", payload.Length, null);
		var interrupted = storage.Allocate("payload.bin", 2);
		var partSize = payload.Length / 2;
		await interrupted.WriteAsync(0, payload.AsMemory(0, 4 * 1024 * 1024), CancellationToken.None);
		await interrupted.DisposeAsync();
		var resumed = GetOpened(storage.GetOrCreate("payload.bin", payload.Length, null));
		var source = new FakeFileSource(payload);
		var sut = CreateSut(out _);

		// Act
		await sut.Download(source, resumed, null, CancellationToken.None).WaitAsync(CompletionTimeout);
		await resumed.DisposeAsync();

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(source.RequestedRanges.OrderBy(x => x.From), Is.EqualTo(new (long From, long To)[]
			{
				(4L * 1024 * 1024, partSize - 1),
				(partSize, payload.Length - 1L),
			}));
			Assert.That(File.ReadAllBytes(resumed.FullPath), Is.EqualTo(payload));
		});
	}

	[Test]
	public async Task Download_OnePartFails_FailsTheDownloadAndStopsRemainingParts()
	{
		// Arrange
		using var database = new SqliteDatabase();
		using var context = database.CreateMigratedGlobalContext();
		using var temp = new TempDirectory();
		using var storage = FileStorageServiceTests.CreateSut(context, temp.Path, out _);
		var payload = CreatePayload(4 * 1024 * 1024);
		storage.GetOrCreate("payload.bin", payload.Length, null);
		await using var opened = storage.Allocate("payload.bin", 4);
		var failingOffset = payload.Length / 4 * 3;
		var source = new FakeFileSource(payload, failingOffset);
		var sut = CreateSut(out _);

		// Act
		var download = sut.Download(source, opened, null, CancellationToken.None);

		// Assert
		Assert.That(async () => await download.WaitAsync(CompletionTimeout), Throws.InstanceOf<IOException>());
		Assert.That(source.CancelledReads, Is.GreaterThan(0));
	}

	[Test]
	public async Task Download_Completed_ReportsFullProgressToTheTracker()
	{
		// Arrange
		using var database = new SqliteDatabase();
		using var context = database.CreateMigratedGlobalContext();
		using var temp = new TempDirectory();
		using var storage = FileStorageServiceTests.CreateSut(context, temp.Path, out _);
		var payload = CreatePayload(4096);
		storage.GetOrCreate("payload.bin", payload.Length, null);
		await using var opened = storage.Allocate("payload.bin", 2);
		var source = new FakeFileSource(payload);
		var sut = CreateSut(out _);
		var reports = new List<DownloadFileProgress>();

		// Act
		await sut.Download(source, opened, progress =>
		{
			lock (reports)
			{
				reports.Add(progress);
			}

			return ValueTask.CompletedTask;
		}, CancellationToken.None).WaitAsync(CompletionTimeout);

		// Assert
		Assert.That(reports, Is.Not.Empty);
		Assert.Multiple(() =>
		{
			Assert.That(reports[^1].DownloadedBytes, Is.EqualTo(payload.Length));
			Assert.That(reports[^1].TotalBytes, Is.EqualTo(payload.Length));
		});
	}

	private static FileSourceDownloader CreateSut(out AutoMocker mocker)
	{
		mocker = new AutoMocker(MockBehavior.Strict);
		mocker.Use<Microsoft.Extensions.Logging.ILogger<FileSourceDownloader>>(NullLogger<FileSourceDownloader>.Instance);
		OptionsStubs.Monitor(mocker, new DownloadSettings());
		return mocker.CreateInstance<FileSourceDownloader>(enablePrivate: true);
	}

	private static byte[] CreatePayload(int size)
	{
		var payload = new byte[size];
		for (var i = 0; i < payload.Length; i++)
		{
			payload[i] = (byte)(i % 251);
		}

		return payload;
	}

	private static OpenedFile GetOpened(IFileStorageService.GetOrCreateResult result) =>
		result switch
		{
			IFileStorageService.GetOrCreateResult.Existing(var descriptor) => descriptor switch
			{
				FileDescriptor.Opened(var opened) => opened,
				FileDescriptor.Finished => throw new AssertionException("Expected an opened file"),
			},
			IFileStorageService.GetOrCreateResult.NeedRehash => throw new AssertionException("Expected a file descriptor"),
			IFileStorageService.GetOrCreateResult.NeedAllocate => throw new AssertionException("Expected a file descriptor"),
		};

	private sealed class FakeFileSource : IFileSource
	{
		private readonly byte[] _content;
		private readonly long? _failAtOffset;
		private readonly bool _supportsOffsets;
		private readonly List<(long From, long To)> _requestedRanges = [];
		private int _cancelledReads;

		public FakeFileSource(byte[] content, long? failAtOffset = null, bool supportsOffsets = true)
		{
			_content = content;
			_failAtOffset = failAtOffset;
			_supportsOffsets = supportsOffsets;
		}

		public IReadOnlyList<(long From, long To)> RequestedRanges
		{
			get
			{
				lock (_requestedRanges)
				{
					return _requestedRanges.ToArray();
				}
			}
		}

		public int CancelledReads => Volatile.Read(ref _cancelledReads);

		public ValueTask<long> GetFileSize(CancellationToken cancellationToken) =>
			ValueTask.FromResult((long)_content.Length);

		public ValueTask<bool> IsOffsetSupported(CancellationToken cancellationToken) =>
			ValueTask.FromResult(_supportsOffsets);

		public ValueTask<Sha256Hash?> GetHash(CancellationToken cancellationToken) =>
			ValueTask.FromResult<Sha256Hash?>(null);

		public ValueTask<IFileStream> OpenStream(
			long? fromInclusive, long? toInclusive, CancellationToken cancellationToken)
		{
			if (!_supportsOffsets && (fromInclusive != null || toInclusive != null))
			{
				throw new InvalidOperationException("Source does not support range requests");
			}

			var from = fromInclusive.GetValueOrDefault();
			var to = toInclusive ?? _content.Length - 1;
			lock (_requestedRanges)
			{
				_requestedRanges.Add((from, to));
			}

			return ValueTask.FromResult<IFileStream>(
				new FakeFileStream(_content, from, to, _failAtOffset, () => Interlocked.Increment(ref _cancelledReads)));
		}
	}

	private sealed class FakeFileStream : IFileStream
	{
		private readonly byte[] _content;
		private readonly long _from;
		private readonly long _to;
		private readonly long? _failAtOffset;
		private readonly Action _onCancelledRead;
		private long _position;

		public FakeFileStream(byte[] content, long from, long to, long? failAtOffset, Action onCancelledRead)
		{
			_content = content;
			_from = from;
			_to = to;
			_failAtOffset = failAtOffset;
			_onCancelledRead = onCancelledRead;
		}

		public long Position => _position;

		public async ValueTask<int> Read(Memory<byte> buffer, CancellationToken cancellationToken)
		{
			// Yielding makes the parts interleave, so a cancelled sibling is observable.
			await Task.Yield();
			if (cancellationToken.IsCancellationRequested)
			{
				_onCancelledRead();
				cancellationToken.ThrowIfCancellationRequested();
			}

			var absolute = _from + _position;
			if (_failAtOffset != null && absolute >= _failAtOffset && absolute < _failAtOffset + buffer.Length)
			{
				throw new IOException("read failed");
			}

			var remaining = _to - absolute + 1;
			var count = (int)Math.Min(buffer.Length, remaining);
			if (count <= 0)
			{
				return 0;
			}

			_content.AsSpan((int)absolute, count).CopyTo(buffer.Span);
			_position += count;
			return count;
		}

		public ValueTask DisposeAsync() => ValueTask.CompletedTask;
	}
}
