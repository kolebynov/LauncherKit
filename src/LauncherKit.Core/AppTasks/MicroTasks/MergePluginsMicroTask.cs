using R3;
using LauncherKit.Core.Backups;
using LauncherKit.Core.FileStorage;
using LauncherKit.Core.Services.Abstractions;

namespace LauncherKit.Core.AppTasks.MicroTasks;

/// <summary>
/// Merges a stored <c>plugins.txt</c> into the selected profile's plugin lists, keeping what the merge needs to be
/// undone in the backup, if any.
/// </summary>
internal sealed class MergePluginsMicroTask : IAppMicroTask
{
	private readonly IFileStorageService _fileStorageService;
	private readonly IModOrganizerService _modOrganizerService;
	private readonly string _pluginsFileName;
	private readonly InstallActionBackup? _backup;

	public MicroTaskDescription Description { get; }

	public MergePluginsMicroTask(IFileStorageService fileStorageService, IModOrganizerService modOrganizerService,
		string pluginsFileName, InstallActionBackup? backup)
	{
		_fileStorageService = fileStorageService;
		_modOrganizerService = modOrganizerService;
		_pluginsFileName = pluginsFileName;
		Description = new MicroTaskDescription.MergePlugins(_pluginsFileName);
		_backup = backup;
	}

	public async ValueTask Execute(ISubject<MicroTaskStep> stepSubject, CancellationToken cancellationToken)
	{
		var pluginsFilePath = _fileStorageService.GetFilePath(_pluginsFileName)
		                      ?? throw new InvalidOperationException($"File {_pluginsFileName} is not stored");
		await _modOrganizerService.MergePlugins(pluginsFilePath, _backup, cancellationToken);
	}
}
