using LauncherKit.Infrastructure;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using LauncherKit.Core.AppTasks;
using LauncherKit.Core.AppTasks.MicroTasks;
using LauncherKit.Core.FileStorage;
using LauncherKit.Core.Services.Abstractions;
using LauncherKit.Core.UserSettings;

namespace LauncherKit.Core.Initialization;

internal sealed class CleanupOldDataInitializer : IConfiguredScopeInitializer
{
	private readonly IAppTaskQueue _appTaskQueue;
	private readonly IFileStorageService _fileStorageService;
	private readonly ITempStorageService _tempStorageService;
	private readonly ILogger<CleanupOldDataInitializer> _logger;
	private readonly TimeProvider _timeProvider;
	private readonly DownloadSettings _downloadSettings;

	public CleanupOldDataInitializer(IAppTaskQueue appTaskQueue, IFileStorageService fileStorageService,
		ITempStorageService tempStorageService, ILogger<CleanupOldDataInitializer> logger,
		IOptions<DownloadSettings> downloadSettings, TimeProvider timeProvider)
	{
		_appTaskQueue = appTaskQueue;
		_fileStorageService = fileStorageService;
		_tempStorageService = tempStorageService;
		_logger = logger;
		_timeProvider = timeProvider;
		_downloadSettings = downloadSettings.Value;
	}

	public async ValueTask Initialize() =>
		await _appTaskQueue.Enqueue(
			new AppTask(
				"CleanupOldData", AppTaskDescription.CleanupOldData.Instance,
				context =>
				[
					context.MicroTasksFactory.CreateDelegateMicroTask(
						(_, _) =>
						{
							RemoveOldDownloads(_fileStorageService);
							return ValueTask.CompletedTask;
						},
						MicroTaskDescription.DeleteOldDownloads.Instance),
					context.MicroTasksFactory.CreateDelegateMicroTask(
						(_, _) =>
						{
							_tempStorageService.DeleteTempData();
							return ValueTask.CompletedTask;
						},
						MicroTaskDescription.DeleteTemporaryFiles.Instance),
				],
				[], null),
			CancellationToken.None);

	private void RemoveOldDownloads(IFileStorageService fileStorageService)
	{
		var finishedStoragePeriod = ToTimeSpan(_downloadSettings.FileStoragePeriod);
		var partialStoragePeriod = ToTimeSpan(_downloadSettings.PartialFileStoragePeriod);
		var now = _timeProvider.GetUtcNow();
		var oldFiles = fileStorageService.GetFileInfos()
			.Where(x => x.LastWriteAt.Add(
				x.Status == StoredFileStatus.Complete ? finishedStoragePeriod : partialStoragePeriod) < now);
		foreach (var fileInfo in oldFiles)
		{
			_logger.LogInformation(
				"Removing old {Status} file {FileName}, last written at {LastWriteAt}",
				fileInfo.Status, fileInfo.FileName, fileInfo.LastWriteAt);
			if (fileStorageService.Delete(fileInfo.FileName) is not IFileStorageService.DeleteResult.Deleted)
			{
				_logger.LogWarning("Old file {FileName} is kept, it cannot be deleted now", fileInfo.FileName);
			}
		}
	}

	private static TimeSpan ToTimeSpan(FileStoragePeriod period) =>
		period switch
		{
			FileStoragePeriod.Day => TimeSpan.FromDays(1),
			FileStoragePeriod.Week => TimeSpan.FromDays(7),
			FileStoragePeriod.Month => TimeSpan.FromDays(30),
			FileStoragePeriod.Unlimited => TimeSpan.FromDays(9999),
			_ => TimeSpan.FromDays(7),
		};
}
