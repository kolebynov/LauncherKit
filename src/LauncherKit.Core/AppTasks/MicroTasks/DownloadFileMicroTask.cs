﻿using LauncherKit.Infrastructure;
using Microsoft.Extensions.Logging;
using R3;
using LauncherKit.Core.Exceptions;
using LauncherKit.Core.Feed;
using LauncherKit.Core.FileSources;
using LauncherKit.Core.FileStorage;
using LauncherKit.Core.Models;
using LauncherKit.Core.Services.Abstractions;

namespace LauncherKit.Core.AppTasks.MicroTasks;

internal sealed class DownloadFileMicroTask : IAppMicroTask
{
	private const int MaxPartCount = 6;
	private const long MinimumPartSize = 128L * 1024 * 1024;

	/// <summary>How long a running download may receive no data before its server counts as failed.</summary>
	internal static readonly TimeSpan StallTimeout = TimeSpan.FromSeconds(15);

	private static readonly MicroTaskStep PrepareStep = new MicroTaskStep.MicroOperation(MicroTaskDescription.Preparing.Instance);

	private readonly DownloadFileSource _downloadFileSource;
	private readonly IDownloadServerFallback _downloadServerFallback;
	private readonly IFeedFileSourceFactory _feedFileSourceFactory;
	private readonly IFileSourceDownloader _fileSourceDownloader;
	private readonly IFileStorageService _fileStorageService;
	private readonly IHttpFileSourceFactory _httpFileSourceFactory;
	private readonly ILogger<DownloadFileMicroTask> _logger;
	private readonly string _targetFileName;
	private readonly TimeProvider _timeProvider;

	public MicroTaskDescription Description { get; }

	public DownloadFileMicroTask(DownloadFileSource downloadFileSource, IFileSourceDownloader fileSourceDownloader,
		IFileStorageService fileStorageService, ILogger<DownloadFileMicroTask> logger,
		IDownloadServerFallback downloadServerFallback, IFeedFileSourceFactory feedFileSourceFactory,
		IHttpFileSourceFactory httpFileSourceFactory, TimeProvider timeProvider)
	{
		_downloadFileSource = downloadFileSource;
		_fileSourceDownloader = fileSourceDownloader;
		_fileStorageService = fileStorageService;
		_logger = logger;
		_downloadServerFallback = downloadServerFallback;
		_feedFileSourceFactory = feedFileSourceFactory;
		_httpFileSourceFactory = httpFileSourceFactory;
		_timeProvider = timeProvider;

		_targetFileName = downloadFileSource switch
		{
			DownloadInfo downloadInfo => downloadInfo.FileName,
			DownloadFileSource.Http http => http.FileName,
		};
		Description = new MicroTaskDescription.DownloadFile(_targetFileName);
	}

	private enum DownloadResult
	{
		Downloaded,
		AlreadyDownloaded,
		Paused,
	}

	public async ValueTask Execute(ISubject<MicroTaskStep> stepSubject, CancellationToken cancellationToken)
	{
		if (_fileStorageService.IsFileFinished(_targetFileName))
		{
			_logger.LogInformation("File {FileName} already downloaded", _targetFileName);
			return;
		}

		_logger.LogInformation("Downloading file {FileName} using downloader {DownloaderType}", _targetFileName, _fileSourceDownloader.GetType().Name);

		while (true)
		{
			stepSubject.OnNext(PrepareStep);
			using var downloadProgress = new DownloadProgress(_targetFileName);

			var result = await Download(stepSubject, downloadProgress, cancellationToken);

			switch (result)
			{
				case DownloadResult.Downloaded:
					_fileStorageService.Complete(_targetFileName);
					_logger.LogInformation("File {FileName} downloaded", _targetFileName);
					return;
				case DownloadResult.AlreadyDownloaded:
					_logger.LogInformation("File {FileName} already downloaded", _targetFileName);
					return;
				case DownloadResult.Paused:
					await WaitForResume(downloadProgress, cancellationToken);
					break;
				default:
					throw new InvalidOperationException($"Unknown download result {result}");
			}
		}
	}

	private static async Task WaitForResume(DownloadProgress downloadProgress, CancellationToken cancellationToken)
	{
		var resumed = new TaskCompletionSource();
		using var _ = cancellationToken.Register(() => resumed.TrySetCanceled());
		using var subscription = downloadProgress.ProgressObservable.Subscribe(
			(downloadProgress, resumed),
			static (_, state) =>
			{
				if (!state.downloadProgress.IsPaused)
				{
					state.resumed.TrySetResult();
				}
			});

		if (downloadProgress.IsPaused)
		{
			await resumed.Task;
		}
	}

	/// <remarks>
	/// Feed files are served by both the main and the proxy server; a plain URL has nowhere to fall back to.
	/// </remarks>
	private Task<DownloadResult> Download(ISubject<MicroTaskStep> stepSubject, DownloadProgress downloadProgress,
		CancellationToken cancellationToken) =>
		_downloadFileSource switch
		{
			DownloadInfo downloadInfo => _downloadServerFallback.Execute(
				(server, ct) => Download(
					_feedFileSourceFactory.Create(downloadInfo, server), stepSubject, downloadProgress, ct),
				cancellationToken),
			DownloadFileSource.Http http => Download(
				_httpFileSourceFactory.Create(http.Uri, null), stepSubject, downloadProgress, cancellationToken),
		};

	/// <exception cref="DownloadStalledException">No data arrived for <see cref="StallTimeout"/>.</exception>
	private async Task<DownloadResult> Download(IFileSource fileSource, ISubject<MicroTaskStep> stepSubject,
		DownloadProgress downloadProgress, CancellationToken cancellationToken)
	{
		await using var destinationDescriptor =
			await OpenDestinationFileForDownload(fileSource, stepSubject, cancellationToken);
		if (destinationDescriptor is not FileDescriptor.Opened(var destination))
		{
			return DownloadResult.AlreadyDownloaded;
		}

		// Started only now: preparing the destination (e.g. rehashing a large file) is not a stalled transfer.
		using var stallCts = new CancellationTokenSource(StallTimeout, _timeProvider);
		// Records that a pause stopped this attempt: IsPaused may already be reset by a quick resume when it unwinds.
		using var pauseCts = new CancellationTokenSource();
		using var downloadCts = CancellationTokenSource.CreateLinkedTokenSource(
			cancellationToken, stallCts.Token, pauseCts.Token);
		using var pauseSubscription = downloadProgress.ProgressObservable.Subscribe(
			(downloadProgress, pauseCts),
			static (_, state) =>
			{
				if (state.downloadProgress.IsPaused)
				{
					state.pauseCts.Cancel();
				}
			});

		var isDownloadProgressSent = false;
		var lastDownloadedBytes = destination.WrittenBytes;
		try
		{
			await _fileSourceDownloader.Download(fileSource, destination,
				progress =>
				{
					if (!isDownloadProgressSent)
					{
						stepSubject.OnNext(new MicroTaskStep.Progress(downloadProgress));
						isDownloadProgressSent = true;
					}

					if (progress.DownloadedBytes > lastDownloadedBytes)
					{
						lastDownloadedBytes = progress.DownloadedBytes;
						stallCts.CancelAfter(StallTimeout);
					}

					downloadProgress.ReportProgress(progress.DownloadedBytes, progress.TotalBytes, progress.Speed);

					return ValueTask.CompletedTask;
				},
				downloadCts.Token);
			return DownloadResult.Downloaded;
		}
		catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
		{
			if (pauseCts.IsCancellationRequested)
			{
				return DownloadResult.Paused;
			}

			if (stallCts.IsCancellationRequested)
			{
				throw new DownloadStalledException(StallTimeout);
			}

			throw;
		}
	}

	private async ValueTask<FileDescriptor> OpenDestinationFileForDownload(
		IFileSource fileSource, ISubject<MicroTaskStep> stepSubject, CancellationToken cancellationToken)
	{
		var getOrCreateResult = _fileStorageService.GetOrCreate(
			_targetFileName, await fileSource.GetFileSize(cancellationToken),
			await fileSource.GetHash(cancellationToken));

		return getOrCreateResult switch
		{
			IFileStorageService.GetOrCreateResult.Existing(var descriptor) => descriptor switch
			{
				FileDescriptor.Opened(var openedFile) =>
					await RestartIfOffsetsAreRequired(fileSource, openedFile, stepSubject, cancellationToken),
				FileDescriptor.Finished => descriptor,
			},
			IFileStorageService.GetOrCreateResult.NeedRehash =>
				await RehashDestination(fileSource, stepSubject, cancellationToken),
			IFileStorageService.GetOrCreateResult.NeedAllocate =>
				new FileDescriptor.Opened(await AllocateDestination(fileSource, stepSubject, cancellationToken)),
		};
	}

	private async ValueTask<FileDescriptor> RehashDestination(
		IFileSource fileSource, ISubject<MicroTaskStep> stepSubject, CancellationToken cancellationToken)
	{
		using var hashProgress = new OperationProgress(MicroTaskDescription.CheckFileHash.Instance);
		stepSubject.OnNext(new MicroTaskStep.Progress(hashProgress));
		var rehashResult = await _fileStorageService.Rehash(
			_targetFileName,
			p =>
			{
				hashProgress.ReportProgress(p);
				return ValueTask.CompletedTask;
			},
			cancellationToken);

		return rehashResult switch
		{
			IFileStorageService.RehashResult.Finished(var fileStream) => new FileDescriptor.Finished(fileStream),
			IFileStorageService.RehashResult.NeedAllocate => await AllocateMismatchedDestination(),
		};

		async Task<FileDescriptor> AllocateMismatchedDestination()
		{
			_logger.LogInformation(
				"File {FileName} found in the downloads directory does not match the expected hash, downloading it",
				_targetFileName);
			return new FileDescriptor.Opened(await AllocateDestination(fileSource, stepSubject, cancellationToken));
		}
	}

	/// <remarks>
	/// A source without byte-range support can only serve the whole file from its beginning, so a
	/// multi-part or partially written download has to start over.
	/// </remarks>
	private async ValueTask<FileDescriptor> RestartIfOffsetsAreRequired(
		IFileSource fileSource, OpenedFile openedFile, ISubject<MicroTaskStep> stepSubject,
		CancellationToken cancellationToken)
	{
		if (openedFile is { WrittenBytes: 0, PartCount: 1 } || await fileSource.IsOffsetSupported(cancellationToken))
		{
			return new FileDescriptor.Opened(openedFile);
		}

		_logger.LogInformation(
			"Source of {FileName} does not support byte ranges, the download starts from the beginning",
			_targetFileName);
		await openedFile.DisposeAsync();
		return new FileDescriptor.Opened(await AllocateDestination(fileSource, stepSubject, cancellationToken));
	}

	private async ValueTask<OpenedFile> AllocateDestination(
		IFileSource fileSource, ISubject<MicroTaskStep> stepSubject, CancellationToken cancellationToken)
	{
		stepSubject.OnNext(new MicroTaskStep.MicroOperation(MicroTaskDescription.PrepareFile.Instance));
		var fileSize = await fileSource.GetFileSize(cancellationToken);
		var isOffsetSupported = await fileSource.IsOffsetSupported(cancellationToken);
		var partCount = isOffsetSupported
			? (int)Math.Clamp(fileSize / MinimumPartSize, 1, MaxPartCount)
			: 1;
		return _fileStorageService.Allocate(_targetFileName, partCount);
	}
}