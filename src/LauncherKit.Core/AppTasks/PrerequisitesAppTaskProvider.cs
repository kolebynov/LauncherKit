using System.Diagnostics;
using LauncherKit.Core.AppTasks.CompleteVerifiers;
using LauncherKit.Core.AppTasks.MicroTasks;
using LauncherKit.Core.Extensions;
using LauncherKit.Core.Feed;
using LauncherKit.Core.FileStorage;
using LauncherKit.Core.Services;
using LauncherKit.Core.Services.Abstractions;
using LauncherKit.Infrastructure;

namespace LauncherKit.Core.AppTasks;

public sealed class PrerequisitesAppTaskProvider : IPrerequisitesAppTaskProvider
{
	private readonly IFeedProvider _feedProvider;
	private readonly IFileStorageService _fileStorageService;
	private readonly IInstallDetector _installDetector;
	private readonly ILaunchersRootDirectoryManager _launchersRootDirectoryManager;

	public IReadOnlyList<AppTask> InstallVcRedistTasks { get; }

	public PrerequisitesAppTaskProvider(IFeedProvider feedProvider, IFileStorageService fileStorageService,
		IInstallDetector installDetector, ILaunchersRootDirectoryManager launchersRootDirectoryManager)
	{
		_feedProvider = feedProvider;
		_fileStorageService = fileStorageService;
		_installDetector = installDetector;
		_launchersRootDirectoryManager = launchersRootDirectoryManager;

		InstallVcRedistTasks =
		[
			CreateInstallVcRedistTask(InstallDetector.VcRedistSupportedArchitecture.X86.Instance),
			CreateInstallVcRedistTask(InstallDetector.VcRedistSupportedArchitecture.X64.Instance),
		];
	}

	public async ValueTask<AppTask> CreateInstallSevenZipTask(CancellationToken cancellationToken)
	{
		var sevenZipDownloadInfo = await _feedProvider.GetSevenZipDownloadInfo(cancellationToken);
		return new AppTask(
			"InstallSevenZip", AppTaskDescription.InstallSevenZip.Instance,
			context =>
			[
				context.MicroTasksFactory.CreateDownloadFileMicroTask(sevenZipDownloadInfo),
				context.MicroTasksFactory.CreateExtractZipMicroTask(sevenZipDownloadInfo.FileName, _launchersRootDirectoryManager.SevenZipDirectory,
					MicroTaskDescription.InstallSevenZip.Instance),
			],
			[], new FileExistsVerifier(_launchersRootDirectoryManager.SevenZipExePath));
	}

	private AppTask CreateInstallVcRedistTask(InstallDetector.VcRedistSupportedArchitecture architecture)
	{
		var fileName = $"vc_redist.{architecture.Name}.exe";
		return new AppTask(
			$"InstallVcRedist_{architecture.Name}", new AppTaskDescription.InstallVcRedist(architecture.Name),
			context =>
			[
				context.MicroTasksFactory.CreateDownloadFileMicroTask(
					new DownloadFileSource.Http(new Uri($"https://aka.ms/vs/17/release/{fileName}"), fileName)),
				context.MicroTasksFactory.CreateRunProcessMicroTask(
					() => new ProcessStartInfo(
						_fileStorageService.GetFilePath(fileName)!, "/install /passive /norestart")
					{
						UseShellExecute = true,
					}, false, MicroTaskDescription.Installing.Instance),
			],
			[],
			new DelegateCompleteVerifier(_ => ValueTask.FromResult(_installDetector.IsVcRedistInstalled(architecture))));
	}
}
