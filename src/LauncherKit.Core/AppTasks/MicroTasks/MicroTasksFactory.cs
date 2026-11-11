using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using R3;
using LauncherKit.Core.AppTasks.ModpackComponents;
using LauncherKit.Core.Backups;
using LauncherKit.Core.Feed.Install;
using LauncherKit.Core.Feed.Mods;
using LauncherKit.Core.FileSources;
using LauncherKit.Core.FileStorage;
using LauncherKit.Core.Fomod;
using LauncherKit.Core.Models;
using LauncherKit.Core.Services.Abstractions;
using LauncherKit.Infrastructure;

namespace LauncherKit.Core.AppTasks.MicroTasks;

public sealed class MicroTasksFactory : IMicroTasksFactory
{
	private readonly IOptionsSnapshot<AppSettings> _appSettingsSnapshot;
	private readonly IDownloadServerFallback _downloadServerFallback;
	private readonly IFileSourceDownloader _fileSourceDownloader;
	private readonly IFileStorageService _fileStorageService;
	private readonly IFileSystemUtils _fileSystemUtils;
	private readonly IFomodInstallService _fomodInstallService;
	private readonly IHttpFileSourceFactory _httpFileSourceFactory;
	private readonly ILoggerFactory _loggerFactory;
	private readonly IModOrganizerService _modOrganizerService;
	private readonly IProcessRunner _processRunner;
	private readonly IFeedFileSourceFactory _feedFileSourceFactory;
	private readonly ISevenZipService _sevenZipService;
	private readonly TimeProvider _timeProvider;

	public MicroTasksFactory(
		IOptionsSnapshot<AppSettings> appSettingsSnapshot, IFileSourceDownloader fileSourceDownloader,
		IFileStorageService fileStorageService, IFileSystemUtils fileSystemUtils, IFomodInstallService fomodInstallService,
		IHttpFileSourceFactory httpFileSourceFactory, ILoggerFactory loggerFactory, IModOrganizerService modOrganizerService,
		IProcessRunner processRunner, IFeedFileSourceFactory feedFileSourceFactory, ISevenZipService sevenZipService,
		IDownloadServerFallback downloadServerFallback, TimeProvider timeProvider)
	{
		_appSettingsSnapshot = appSettingsSnapshot;
		_fileSourceDownloader = fileSourceDownloader;
		_fileStorageService = fileStorageService;
		_fileSystemUtils = fileSystemUtils;
		_fomodInstallService = fomodInstallService;
		_httpFileSourceFactory = httpFileSourceFactory;
		_loggerFactory = loggerFactory;
		_modOrganizerService = modOrganizerService;
		_processRunner = processRunner;
		_feedFileSourceFactory = feedFileSourceFactory;
		_sevenZipService = sevenZipService;
		_downloadServerFallback = downloadServerFallback;
		_timeProvider = timeProvider;
	}

#pragma warning disable CA1822
	public IAppMicroTask CreateDelegateMicroTask(
#pragma warning restore CA1822
		Func<ISubject<MicroTaskStep>, CancellationToken, ValueTask> taskFunc, MicroTaskDescription description) =>
		new DelegateMicroTask(description, taskFunc);

	public IAppMicroTask CreateDeleteFileSystemItemsMicroTask(IReadOnlyCollection<SkyrimRelativePath> paths,
		bool failOnFailedDelete,
		MicroTaskDescription description) =>
		new DeleteFileSystemItemsMicroTask(_fileSystemUtils, description, paths,
			_appSettingsSnapshot.Value.SkyrimDirectory!, failOnFailedDelete);

	public IAppMicroTask CreateDownloadFileMicroTask(DownloadFileSource downloadFileSource) =>
		new DownloadFileMicroTask(downloadFileSource, _fileSourceDownloader, _fileStorageService,
			_loggerFactory.CreateLogger<DownloadFileMicroTask>(), _downloadServerFallback, _feedFileSourceFactory,
			_httpFileSourceFactory, _timeProvider);

	public IAppMicroTask CreateExtractSevenZipMicroTask(string archiveName, SkyrimRelativePath destinationPath,
		MicroTaskDescription? description = null) =>
		new ExtractSevenZipMicroTask(_fileStorageService, _sevenZipService, description, archiveName, destinationPath,
			_appSettingsSnapshot.Value.SkyrimDirectory!);

	public IAppMicroTask CreateExtractSevenZipExceptMicroTask(string archiveName, SkyrimRelativePath destinationPath,
		string excludedEntryPath, MicroTaskDescription description) =>
		new ExtractSevenZipMicroTask(_fileStorageService, _sevenZipService, description, archiveName, destinationPath,
			_appSettingsSnapshot.Value.SkyrimDirectory!, excludedEntryPath);

	public IAppMicroTask CreateExtractSevenZipEntryMicroTask(string archiveName, SkyrimRelativePath destinationPath,
		string entryPath, MicroTaskDescription description) =>
		new ExtractSevenZipEntryMicroTask(_fileStorageService, _sevenZipService, description, archiveName, destinationPath,
			_appSettingsSnapshot.Value.SkyrimDirectory!, entryPath);

	public IAppMicroTask CreateExtractZipMicroTask(string archiveName, string destinationDirectory,
		MicroTaskDescription description) =>
		new ExtractZipMicroTask(description, _fileStorageService, archiveName, destinationDirectory);

	public IAppMicroTask CreateInstallModMicroTask(string modName, string archiveName, ModPlacement? placement) =>
		new InstallModMicroTask(_fileStorageService, _fomodInstallService,
			_loggerFactory.CreateLogger<InstallModMicroTask>(), _modOrganizerService, modName, archiveName, placement);

	public IAppMicroTask CreateMergePluginsMicroTask(string pluginsFileName, InstallActionBackup? backup) =>
		new MergePluginsMicroTask(_fileStorageService, _modOrganizerService, pluginsFileName, backup);

	public IAppMicroTask CreateRunProcessMicroTask(Func<ProcessStartInfo> processStartInfoProvider, bool throwForNonZeroExitCode,
		MicroTaskDescription description) =>
		new RunProcessMicroTask(description, _processRunner, processStartInfoProvider, throwForNonZeroExitCode);

	public IAppMicroTask CreateSelectRequiredModMicroTask(InstallStepContext context, IReadOnlyList<string> requiredMods) =>
		new SelectRequiredModMicroTask(context, _loggerFactory.CreateLogger<SelectRequiredModMicroTask>(),
			_modOrganizerService, requiredMods);

	public IAppMicroTask CreateSelectChoicesMicroTask(InstallStepContext context, InstallAction.SelectChoices action) =>
		new SelectChoicesMicroTask(context, _loggerFactory.CreateLogger<SelectChoicesMicroTask>(), action);

	public IAppMicroTask CreateSelectModpackComponentsMicroTask(ModpackComponentsOffer offer) =>
		new SelectModpackComponentsMicroTask(offer, _downloadServerFallback, _feedFileSourceFactory);
}
