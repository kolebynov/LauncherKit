using LauncherKit.Core.Services.Abstractions;
using LauncherKit.Infrastructure;

namespace LauncherKit.Core.Services;

public sealed class AppSettingsOverrideFileStore : ISettingsFileStore, IDisposable
{
	private readonly string _filePath;
	private readonly IFileSystemUtils _fileSystemUtils;
	private readonly SemaphoreSlim _writeLock = new(1, 1);

	public AppSettingsOverrideFileStore(IFileSystemUtils fileSystemUtils)
		: this(LauncherConstants.SettingsOverrideFileName, fileSystemUtils)
	{
	}

	internal AppSettingsOverrideFileStore(string filePath, IFileSystemUtils fileSystemUtils)
	{
		_filePath = filePath;
		_fileSystemUtils = fileSystemUtils;
	}

	public async Task<string?> Read(CancellationToken cancellationToken)
	{
		if (!File.Exists(_filePath))
		{
			return null;
		}

		return await File.ReadAllTextAsync(_filePath, cancellationToken);
	}

	public async Task Write(string json, CancellationToken cancellationToken)
	{
		// Writes of this process are applied in call order; the unique temporary file keeps writes
		// of other launcher processes from touching it. The file is replaced only once fully written,
		// so an interrupted write keeps the previous settings.
		await _writeLock.WaitAsync(cancellationToken);
		var tempPath = $"{_filePath}.{Guid.NewGuid():N}.tmp";
		try
		{
			await _fileSystemUtils.WriteAllText(tempPath, json, cancellationToken);
			_fileSystemUtils.MoveFile(tempPath, _filePath, overwrite: true);
		}
		catch
		{
			_fileSystemUtils.SafeDeleteFile(tempPath);
			throw;
		}
		finally
		{
			_writeLock.Release();
		}
	}

	public void Dispose() => _writeLock.Dispose();
}
