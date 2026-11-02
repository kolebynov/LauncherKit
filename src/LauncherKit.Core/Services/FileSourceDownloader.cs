using System.Buffers;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using LauncherKit.Core.Extensions;
using LauncherKit.Core.FileSources;
using LauncherKit.Core.FileStorage;
using LauncherKit.Core.Services.Abstractions;
using LauncherKit.Core.UserSettings;

namespace LauncherKit.Core.Services;

internal sealed class FileSourceDownloader : IFileSourceDownloader
{
	private const int ReadBufferSize = 64 * 1024;

	private readonly ILogger<FileSourceDownloader> _logger;
	private DownloadSettings _downloadSettings = null!;

	public FileSourceDownloader(
		ILogger<FileSourceDownloader> logger, IOptionsMonitor<DownloadSettings> downloadSettings)
	{
		_logger = logger;
		downloadSettings.UpdateFieldOnChange(() => ref _downloadSettings!);
	}

	public async Task Download(IFileSource fileSource, OpenedFile openedFile,
		Func<DownloadFileProgress, ValueTask>? progressTracker, CancellationToken cancellationToken)
	{
		var progressReporter = new DownloadFileProgressReporter(progressTracker, openedFile.Size, openedFile.WrittenBytes);
		var pendingParts = openedFile.FileParts.Where(x => x.WrittenBytes < x.Size).ToArray();
		if (pendingParts.Length == 0)
		{
			await progressReporter.ReportCompletion();
			return;
		}

		// A failing part must stop its siblings, otherwise they keep downloading into a file that is
		// already known to be unusable.
		var partsCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		try
		{
			var tasks = pendingParts
				.Select(filePart => DownloadPart(fileSource, openedFile, filePart, progressReporter, partsCts))
				.ToArray();

			await Task.WhenAll(tasks);
		}
		catch (Exception e) when (e is not OperationCanceledException)
		{
			_logger.LogError(e, "Downloading of {FilePath} failed", openedFile.FullPath);
			throw;
		}
		finally
		{
			partsCts.Dispose();
		}

		cancellationToken.ThrowIfCancellationRequested();
		await progressReporter.ReportCompletion();
	}

	private static async Task DownloadPart(
		IFileSource fileSource, OpenedFile openedFile, OpenedFile.FilePartInfo filePart,
		DownloadFileProgressReporter progressReporter, CancellationTokenSource partsCts)
	{
		var cancellationToken = partsCts.Token;
		try
		{
			// A part that covers the whole file needs no range at all, so a source without byte-range
			// support can serve it with a plain 200 response.
			var isWholeFile = filePart is { Offset: 0, WrittenBytes: 0 } && filePart.Size == openedFile.Size;
			var from = isWholeFile ? null : (long?)(filePart.Offset + filePart.WrittenBytes);
			var toInclusive = isWholeFile ? null : (long?)(filePart.Offset + filePart.Size - 1);
			await using var downloadStream = await fileSource.OpenStream(from, toInclusive, cancellationToken);

			var buffer = ArrayPool<byte>.Shared.Rent(ReadBufferSize);
			try
			{
				var bufferMemory = buffer.AsMemory(0, ReadBufferSize);
				while (true)
				{
					var bytesRead = await downloadStream.Read(bufferMemory, cancellationToken);
					if (bytesRead == 0)
					{
						break;
					}

					await openedFile.WriteAsync(filePart.Index, bufferMemory[..bytesRead], cancellationToken);
					await progressReporter.ReportProgress(bytesRead);
				}
			}
			finally
			{
				ArrayPool<byte>.Shared.Return(buffer);
			}
		}
		catch (Exception)
		{
			await partsCts.CancelAsync();
			throw;
		}
	}

	private sealed class DownloadFileProgressReporter
	{
		private static readonly TimeSpan ReportInterval = TimeSpan.FromSeconds(1);

		private readonly Func<DownloadFileProgress, ValueTask>? _progressTracker;
		private readonly long _size;
		private long _downloadedBytes;
		private long _lastReportTimestamp;
		private long _lastReportedBytes;
		private int _isReporting;

		public DownloadFileProgressReporter(Func<DownloadFileProgress, ValueTask>? progressTracker, long size, long downloadedBytes)
		{
			_progressTracker = progressTracker;
			_size = size;
			_lastReportedBytes = _downloadedBytes = downloadedBytes;
			_lastReportTimestamp = Stopwatch.GetTimestamp();
		}

		public async ValueTask ReportProgress(long downloaded)
		{
			var downloadedBytes = Interlocked.Add(ref _downloadedBytes, downloaded);
			if (_progressTracker == null || Interlocked.CompareExchange(ref _isReporting, 1, 0) != 0)
			{
				return;
			}

			// Only the thread that owns the reporting flag touches the fields below.
			try
			{
				var duration = Stopwatch.GetElapsedTime(_lastReportTimestamp);
				if (duration < ReportInterval)
				{
					return;
				}

				await Report(downloadedBytes, duration);
			}
			finally
			{
				Volatile.Write(ref _isReporting, 0);
			}
		}

		/// <summary>
		/// Reports the final state, so the tracker is never left below 100% when the last bytes arrive
		/// within the reporting interval.
		/// </summary>
		public async ValueTask ReportCompletion()
		{
			if (_progressTracker == null)
			{
				return;
			}

			while (Interlocked.CompareExchange(ref _isReporting, 1, 0) != 0)
			{
				await Task.Yield();
			}

			try
			{
				await Report(Interlocked.Read(ref _downloadedBytes), Stopwatch.GetElapsedTime(_lastReportTimestamp));
			}
			finally
			{
				Volatile.Write(ref _isReporting, 0);
			}
		}

		private ValueTask Report(long downloadedBytes, TimeSpan duration)
		{
			var speed = duration > TimeSpan.Zero ? (downloadedBytes - _lastReportedBytes) / duration.TotalSeconds : 0;
			_lastReportTimestamp = Stopwatch.GetTimestamp();
			_lastReportedBytes = downloadedBytes;
			return _progressTracker!(new DownloadFileProgress(downloadedBytes, _size, speed));
		}
	}
}
