using System.IO.Compression;
using R3;
using LauncherKit.Core.FileStorage;

namespace LauncherKit.Core.AppTasks.MicroTasks;

internal sealed class ExtractZipMicroTask : IAppMicroTask
{
	private readonly IFileStorageService _fileStorageService;
	private readonly string _fileName;
	private readonly string _destinationDirectory;

	public MicroTaskDescription Description { get; }

	public ExtractZipMicroTask(MicroTaskDescription description, IFileStorageService fileStorageService, string fileName, string destinationDirectory)
	{
		Description = description;
		_fileStorageService = fileStorageService;
		_fileName = fileName;
		_destinationDirectory = destinationDirectory;
	}

	public async ValueTask Execute(ISubject<MicroTaskStep> stepSubject, CancellationToken cancellationToken)
	{
		using var fileStream = _fileStorageService.Get(_fileName);
		if (fileStream == null)
		{
			throw new InvalidOperationException("File must be finished");
		}

		await Task.Factory.StartNew(
			() => ZipFile.ExtractToDirectory(fileStream, _destinationDirectory, true), cancellationToken,
			TaskCreationOptions.LongRunning, TaskScheduler.Current);
	}
}