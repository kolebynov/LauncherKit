using R3;
using LauncherKit.Core.Extensions;
using LauncherKit.Core.Models;
using LauncherKit.Infrastructure;

namespace LauncherKit.Core.AppTasks.MicroTasks;

internal sealed class DeleteFileSystemItemsMicroTask : IAppMicroTask
{
	private readonly IFileSystemUtils _fileSystemUtils;
	private readonly IReadOnlyCollection<string> _paths;
	private readonly bool _failOnFailedDelete;

	public MicroTaskDescription Description { get; }

	public DeleteFileSystemItemsMicroTask(IFileSystemUtils fileSystemUtils, MicroTaskDescription description,
		IReadOnlyCollection<SkyrimRelativePath> relativePaths, string skyrimDirectory, bool failOnFailedDelete)
	{
		_fileSystemUtils = fileSystemUtils;
		_paths = relativePaths.Select(x => x.ToAbsolute(skyrimDirectory)).ToArray();
		Description = description;
		_failOnFailedDelete = failOnFailedDelete;
	}

	public ValueTask Execute(ISubject<MicroTaskStep> stepSubject, CancellationToken cancellationToken)
	{
		var task = Task.Factory.StartNew(
			() =>
			{
				foreach (var path in _paths)
				{
					var result = File.Exists(path)
						? _fileSystemUtils.SafeDeleteFile(path)
						: _fileSystemUtils.SafeDeleteDirectory(path);
					if (result is FileSystemUtils.SafeDeleteResult.Error(var exception) && _failOnFailedDelete)
					{
						throw new LauncherKit.Core.Exceptions.FileSystemItemDeletionException(path, exception);
					}
				}
			},
			cancellationToken, TaskCreationOptions.LongRunning, TaskScheduler.Current);
		return task.AsValueTask();
	}
}