using Microsoft.Extensions.Options;
using LauncherKit.Core.Extensions;
using LauncherKit.Core.Services.Abstractions;
using LauncherKit.Infrastructure;

namespace LauncherKit.Core.Services;

public sealed class TempStorageService : ITempStorageService
{
	private readonly IFileSystemUtils _fileSystemUtils;
	private AppSettings _appSettings = null!;

	public TempStorageService(IOptionsMonitor<AppSettings> appSettingsMonitor, IFileSystemUtils fileSystemUtils)
	{
		_fileSystemUtils = fileSystemUtils;
		appSettingsMonitor.UpdateFieldOnChange(() => ref _appSettings!);
	}

	public TempDirectoryHandle CreateTempDirectory()
	{
		var tempDataDirectory = _appSettings.GetTempDataDirectory();
		if (tempDataDirectory == null)
		{
			throw new InvalidOperationException("Temp data directory is not set");
		}

		var tempDirectory = Path.Combine(tempDataDirectory, Path.GetRandomFileName());
		_fileSystemUtils.EnsureDirectoryExists(tempDirectory);

		return new TempDirectoryHandle(tempDirectory, _fileSystemUtils);
	}

	public void DeleteTempData()
	{
		var tempDataDirectory = _appSettings.GetTempDataDirectory();
		if (tempDataDirectory != null)
		{
			_fileSystemUtils.SafeDeleteDirectory(tempDataDirectory);
		}
	}

	public readonly struct TempDirectoryHandle : IDisposable
	{
		private readonly IFileSystemUtils _fileSystemUtils;

		public string Path { get; }

		public TempDirectoryHandle(string tempDirectory, IFileSystemUtils fileSystemUtils)
		{
			Path = tempDirectory;
			_fileSystemUtils = fileSystemUtils;
		}

		public void Dispose()
		{
			if (Path != null)
			{
				_fileSystemUtils.SafeDeleteDirectory(Path);
			}
		}
	}
}