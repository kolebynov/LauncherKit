using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using LauncherKit.Core.Db;
using LauncherKit.Core.Exceptions;
using LauncherKit.Core.Extensions;
using LauncherKit.Core.Models;
using LauncherKit.Core.Utils;
using LauncherKit.Infrastructure;
using static LauncherKit.Core.FileStorage.IFileStorageService;

namespace LauncherKit.Core.FileStorage;

public sealed class FileStorageService : IFileStorageService
{
	/// <summary>
	/// Extension of the file the launcher downloads into. The download is published under its final name
	/// only when it is complete, so a file at the final path is either a finished download or a copy the
	/// user placed into the downloads directory.
	/// </summary>
	internal const string InProgressFileExtension = ".part";

	private const int ErrorSharingViolationHResult = unchecked((int)0x80070020);
	private const int ErrorLockViolationHResult = unchecked((int)0x80070021);

	private const string FileColumns = """ "FileName", "Path", "Size", "CreatedAt", "Hash", "FinishedAt" """;
	private const string SelectFilesSql = $"""SELECT {FileColumns} FROM "{DbConstants.FilesTableName}" """;
	private const string SelectFileSql = $"""{SelectFilesSql} WHERE "FileName" = $fileName LIMIT 1""";
	private const string SelectUnfinishedFilesSql = $"""{SelectFilesSql} WHERE "FinishedAt" IS NULL""";

	private const string InsertFileSql =
		$"""
		 INSERT INTO "{DbConstants.FilesTableName}" ({FileColumns})
		 VALUES ($fileName, $path, $size, $createdAt, $hash, $finishedAt)
		 """;

	private const string UpdateFileHashSql =
		$"""UPDATE "{DbConstants.FilesTableName}" SET "Hash" = $hash WHERE "FileName" = $fileName""";

	private const string UpdateFileFinishedAtSql =
		$"""UPDATE "{DbConstants.FilesTableName}" SET "FinishedAt" = $finishedAt WHERE "FileName" = $fileName""";

	private const string DeleteFileSql = $"""DELETE FROM "{DbConstants.FilesTableName}" WHERE "FileName" = $fileName""";

	private const string FilePartColumns = """ "FileName", "Index", "Size", "WrittenBytes", "LastWriteAt" """;
	private const string SelectFilePartsSql = $"""SELECT {FilePartColumns} FROM "{DbConstants.FilePartsTableName}" """;
	private const string SelectFilePartsOfFileSql = $"""{SelectFilePartsSql} WHERE "FileName" = $fileName""";

	private const string InsertFilePartSql =
		$"""
		 INSERT INTO "{DbConstants.FilePartsTableName}" ({FilePartColumns})
		 VALUES ($fileName, $index, $size, $writtenBytes, $lastWriteAt)
		 """;

	private const string DeleteFilePartsSql =
		$"""DELETE FROM "{DbConstants.FilePartsTableName}" WHERE "FileName" = $fileName""";

	private readonly GlobalDbContext _dbContext;
	private readonly IDiskSpaceChecker _diskSpaceChecker;
	private readonly IFileSystemUtils _fileSystemUtils;
	private readonly IHashCalculator _hashCalculator;
	private readonly TimeProvider _timeProvider;
	private readonly IDisposable _settingsChangeSubscription;
	private readonly ILogger<FileStorageService> _logger;
	private AppSettings _appSettings = null!;

	public FileStorageService(IOptionsMonitor<AppSettings> appSettingsMonitor, GlobalDbContext dbContext,
		IDiskSpaceChecker diskSpaceChecker, IFileSystemUtils fileSystemUtils, IHashCalculator hashCalculator,
		TimeProvider timeProvider, ILogger<FileStorageService> logger)
	{
		_dbContext = dbContext;
		_diskSpaceChecker = diskSpaceChecker;
		_fileSystemUtils = fileSystemUtils;
		_hashCalculator = hashCalculator;
		_timeProvider = timeProvider;
		_logger = logger;
		_settingsChangeSubscription = appSettingsMonitor.UpdateFieldOnChange(() => ref _appSettings!);
	}

	public FileStream? Get(string uniqueFileName)
	{
		var filePath = GetFilePath(uniqueFileName);
		return filePath != null ? OpenForRead(filePath) : null;
	}

	public GetOrCreateResult GetOrCreate(string uniqueFileName, long size, Sha256Hash? hash)
	{
#pragma warning disable CA2000
		var appSettings = _appSettings;
		_fileSystemUtils.EnsureDirectoryExists(appSettings.GetDownloadsDirectory()!);

		var fileInfo = GetFileInfo(uniqueFileName);

		if (fileInfo == null)
		{
			var path = Path.Combine(appSettings.GetDownloadsDirectory()!, uniqueFileName);
			fileInfo = new FileInfo
			{
				FileName = uniqueFileName,
				Path = path,
				Size = size,
				Hash = hash,
				CreatedAt = _timeProvider.GetUtcNow(),
			};
			InsertFileInfo(fileInfo);
		}

		if (fileInfo.Size != size)
		{
			throw new InvalidOperationException($"Provided file size does not match the file size stored in the database, provided file size = {size}, stored file size = {fileInfo.Size}");
		}

		if (fileInfo.Hash == null && hash != null)
		{
			_dbContext.Execute(UpdateFileHashSql, ("$hash", hash.Value.ToString()), ("$fileName", uniqueFileName));
			fileInfo = FindFileInfo(uniqueFileName)!;
		}

		if (fileInfo.Hash != hash)
		{
			throw new InvalidOperationException("Provided file hash does not match the file hash stored in the database");
		}

		if (fileInfo.FinishedAt != null)
		{
			if (_fileSystemUtils.GetFileSize(fileInfo.Path) == fileInfo.Size)
			{
				return new GetOrCreateResult.Existing(new FileDescriptor.Finished(OpenForRead(fileInfo.Path)));
			}

			_logger.LogWarning(
				"Finished file {FileName} does not match the expected size anymore, downloading it again",
				uniqueFileName);
			DeleteOrThrow(uniqueFileName);
			return GetOrCreate(uniqueFileName, size, hash);
		}

		// The download is still in progress, so anything at the final path was placed there from outside
		// the launcher and can only be adopted after its hash is verified.
		if (File.Exists(fileInfo.Path))
		{
			DiscardInProgressState(fileInfo);
			if (fileInfo.Hash != null && _fileSystemUtils.GetFileSize(fileInfo.Path) == fileInfo.Size)
			{
				return GetOrCreateResult.NeedRehash.Instance;
			}

			_logger.LogInformation(
				"Externally placed file {FileName} cannot be used, it will be downloaded again", uniqueFileName);
			_fileSystemUtils.SafeDeleteFile(fileInfo.Path);
			return GetOrCreateResult.NeedAllocate.Instance;
		}

		var inProgressPath = GetInProgressPath(fileInfo.Path);
		var fileParts = GetFileParts(fileInfo.FileName);
		if (fileParts.Length > 0 && File.Exists(inProgressPath))
		{
			return new GetOrCreateResult.Existing(
				new FileDescriptor.Opened(new OpenedFile(inProgressPath, fileParts, _dbContext, _timeProvider)));
		}

		DiscardInProgressState(fileInfo);
		return GetOrCreateResult.NeedAllocate.Instance;
#pragma warning restore CA2000
	}

	public OpenedFile Allocate(string uniqueFileName, int partCount)
	{
		_logger.LogInformation("Allocating space for {FileName}", uniqueFileName);

		if (partCount <= 0)
		{
			throw new ArgumentOutOfRangeException(nameof(partCount), "Part count must be greater than 0");
		}

		var fileInfo = GetFileInfo(uniqueFileName, true);
		if (fileInfo == null)
		{
			throw new InvalidOperationException("File not found for allocating");
		}

		if (fileInfo.FinishedAt != null)
		{
			throw new InvalidOperationException("File is already finished");
		}

		var inProgressPath = GetInProgressPath(fileInfo.Path);

		// An allocation always starts a new download, so the previous reservation is released first:
		// otherwise its bytes still count as used while the space for the new one is checked.
		_fileSystemUtils.SafeDeleteFile(inProgressPath);

		// The whole file is reserved below, so the space has to be available before the reservation, not
		// after it: the reserved bytes no longer count as free.
		_diskSpaceChecker.CheckAvailableDiskSpace(fileInfo.Path, fileInfo.Size);

		using (var partFile = new FileStream(inProgressPath, new FileStreamOptions
		{
			Mode = FileMode.Create,
			Access = FileAccess.ReadWrite,
			Share = FileShare.None,
			BufferSize = 0,
			PreallocationSize = fileInfo.Size,
		}))
		{
			partFile.SetLength(fileInfo.Size);
		}

		// A part can never be smaller than one byte, and the remainder of an uneven split goes to the last part.
		var effectivePartCount = (int)Math.Clamp(partCount, 1, Math.Max(fileInfo.Size, 1));
		var partSize = fileInfo.Size / effectivePartCount;
		var lastPartSize = fileInfo.Size - (partSize * (effectivePartCount - 1));
		var allocatedAt = _timeProvider.GetUtcNow();
		var fileParts = Enumerable.Range(0, effectivePartCount - 1)
			.Select(x => new FilePart
			{
				FileName = fileInfo.FileName,
				Index = x,
				Size = partSize,
				WrittenBytes = 0,
				LastWriteAt = allocatedAt,
			})
			.Append(new FilePart
			{
				FileName = fileInfo.FileName,
				Index = effectivePartCount - 1,
				Size = lastPartSize,
				WrittenBytes = 0,
				LastWriteAt = allocatedAt,
			})
			.ToArray();

		_dbContext.Execute(DeleteFilePartsSql, ("$fileName", fileInfo.FileName));
		foreach (var filePart in fileParts)
		{
			InsertFilePart(filePart);
		}

		return new OpenedFile(inProgressPath, fileParts, _dbContext, _timeProvider);
	}

	public async Task<RehashResult> Rehash(
		string uniqueFileName, Func<double, ValueTask>? progressTracker, CancellationToken cancellationToken)
	{
#pragma warning disable CA2000
		_logger.LogInformation("Rehashing file {FileName}", uniqueFileName);

		var fileInfo = GetFileInfo(uniqueFileName);
		if (fileInfo == null)
		{
			throw new InvalidOperationException("File not found for rehashing");
		}

		if (fileInfo.Hash == null)
		{
			throw new InvalidOperationException("File hash is not set");
		}

		if (fileInfo.FinishedAt != null)
		{
			throw new InvalidOperationException("File is already finished");
		}

		if (fileInfo.Size != _fileSystemUtils.GetFileSize(fileInfo.Path))
		{
			throw new InvalidOperationException("File size does not match the actual file size");
		}

		// A file at the final path supersedes anything the launcher had downloaded, whether it is adopted
		// or rejected below.
		DiscardInProgressState(fileInfo);

		var calculatedHash = await _hashCalculator.HashFile(fileInfo.Path, progressTracker, cancellationToken);
		var hashMatches = calculatedHash == fileInfo.Hash;

		_logger.LogInformation(
			"Rehashing finished, hash matches = {HashMatches}, file hash = {FileHash}, calculated hash = {CalculatedHash}",
			hashMatches, fileInfo.Hash.Value.ToString(), calculatedHash.ToString());

		if (hashMatches)
		{
			MarkFinished(fileInfo);
			return new RehashResult.Finished(OpenForRead(fileInfo.Path));
		}

		_fileSystemUtils.SafeDeleteFile(fileInfo.Path);
		return RehashResult.NeedAllocate.Instance;
#pragma warning restore CA2000
	}

	public void Complete(string uniqueFileName)
	{
		var fileInfo = GetFileInfo(uniqueFileName, true);
		if (fileInfo == null)
		{
			throw new InvalidOperationException("File not found for completing");
		}

		if (fileInfo.FinishedAt != null)
		{
			return;
		}

		var inProgressPath = GetInProgressPath(fileInfo.Path);
		var writtenBytes = GetFileParts(fileInfo.FileName).Select(x => x.WrittenBytes).ToArray();
		if (writtenBytes.Length == 0 || writtenBytes.Sum() != fileInfo.Size ||
			_fileSystemUtils.GetFileSize(inProgressPath) != fileInfo.Size)
		{
			throw new InvalidOperationException(
				$"File {uniqueFileName} cannot be completed, the download is not finished");
		}

		_fileSystemUtils.MoveFile(inProgressPath, fileInfo.Path, true);
		MarkFinished(fileInfo);
		_logger.LogInformation("File {FileName} is completed", uniqueFileName);
	}

	public void MigrateLegacyPartialDownloads()
	{
		var unfinishedFiles = _dbContext.Read(SelectUnfinishedFilesSql, ReadFileInfo);
		foreach (var fileInfo in unfinishedFiles)
		{
			var inProgressPath = GetInProgressPath(fileInfo.Path);
			if (File.Exists(inProgressPath))
			{
				continue;
			}

			// Launchers without the in-progress extension appended to the final path without preallocating,
			// so the legacy partial file is exactly as long as the progress recorded for it. Anything else
			// at the final path was placed there from outside the launcher and must not be adopted here.
			var writtenBytes = GetFileParts(fileInfo.FileName).Select(x => x.WrittenBytes).ToArray();
			if (writtenBytes.Length == 0 || _fileSystemUtils.GetFileSize(fileInfo.Path) != writtenBytes.Sum())
			{
				continue;
			}

			_logger.LogInformation(
				"Moving partial download of {FileName} to {InProgressPath}", fileInfo.FileName, inProgressPath);
			_fileSystemUtils.MoveFile(fileInfo.Path, inProgressPath);
		}
	}

	public string? GetFilePath(string uniqueFileName) => GetFileInfo(uniqueFileName)?.Path;

	public IReadOnlyList<StoredFileInfo> GetFileInfos()
	{
		var partsLastWriteAt = _dbContext.Read(SelectFilePartsSql, ReadFilePart)
			.GroupBy(x => x.FileName)
			.ToDictionary(x => x.Key, x => x.Max(part => part.LastWriteAt));
		return _dbContext.Read(SelectFilesSql, ReadFileInfo)
			.Where(CheckForFileExistence)
			.Select(fileInfo => new StoredFileInfo
			{
				FileName = fileInfo.FileName,
				Size = fileInfo.Size,
				Status = fileInfo.FinishedAt != null ? StoredFileStatus.Complete : StoredFileStatus.Partial,
				LastWriteAt = fileInfo.FinishedAt
					?? partsLastWriteAt.GetValueOrDefault(fileInfo.FileName, fileInfo.CreatedAt),
			})
			.ToList();
	}

	public bool IsFileFinished(string uniqueFileName) =>
		GetFileInfo(uniqueFileName)?.FinishedAt != null;

	public DeleteResult Delete(string uniqueFileName)
	{
		var fileInfo = FindFileInfo(uniqueFileName);
		if (fileInfo == null)
		{
			return DeleteResult.Deleted.Instance;
		}

		// The records are kept until both copies are gone, so a failed deletion leaves the file listed
		// and the user can retry it.
		var failure = DeleteFromDisk(fileInfo.Path) ?? DeleteFromDisk(GetInProgressPath(fileInfo.Path));
		if (failure != null)
		{
			return failure.Value;
		}

		_dbContext.Execute(DeleteFileSql, ("$fileName", uniqueFileName));
		_dbContext.Execute(DeleteFilePartsSql, ("$fileName", uniqueFileName));
		return DeleteResult.Deleted.Instance;
	}

	public void Dispose()
	{
		_settingsChangeSubscription.Dispose();
	}

	internal static string GetInProgressPath(string filePath) => filePath + InProgressFileExtension;

	private DeleteResult? DeleteFromDisk(string path)
	{
		if (_fileSystemUtils.SafeDeleteFile(path) is not FileSystemUtils.SafeDeleteResult.Error(var exception))
		{
			return null;
		}

		_logger.LogWarning(exception, "Failed to delete file {Path}", path);
		return exception switch
		{
			IOException { HResult: ErrorSharingViolationHResult or ErrorLockViolationHResult } =>
				new DeleteResult.FileInUse(path),
			UnauthorizedAccessException => new DeleteResult.AccessDenied(path),
			_ => new DeleteResult.Error(path, exception),
		};
	}

	private void DeleteOrThrow(string uniqueFileName)
	{
		Exception? failure = Delete(uniqueFileName) switch
		{
			DeleteResult.Deleted => null,
			DeleteResult.FileInUse(var path) => new StoredFileInUseException(path),
			DeleteResult.AccessDenied(var path) => new StoredFileAccessDeniedException(path),
			DeleteResult.Error(var path, var exception) => new StoredFileDeletionException(path, exception),
		};
		if (failure != null)
		{
			throw failure;
		}
	}

	/// <summary>A finished file is published under its final name and keeps no in-progress leftovers.</summary>
	private void MarkFinished(FileInfo fileInfo)
	{
		DiscardInProgressState(fileInfo);
		_dbContext.Execute(UpdateFileFinishedAtSql,
			("$finishedAt", _timeProvider.GetUtcNow().ToUnixTimeSeconds()), ("$fileName", fileInfo.FileName));
	}

	private void DiscardInProgressState(FileInfo fileInfo)
	{
		_fileSystemUtils.SafeDeleteFile(GetInProgressPath(fileInfo.Path));
		_dbContext.Execute(DeleteFilePartsSql, ("$fileName", fileInfo.FileName));
	}

	private FileInfo? GetFileInfo(string uniqueFileName, bool skipFileExistenceCheck = false)
	{
		var fileInfo = FindFileInfo(uniqueFileName);
		if (fileInfo == null)
		{
			return null;
		}

		return skipFileExistenceCheck || CheckForFileExistence(fileInfo) ? fileInfo : null;
	}

	private bool CheckForFileExistence(FileInfo fileInfo)
	{
		if (File.Exists(fileInfo.Path) ||
			(fileInfo.FinishedAt == null && File.Exists(GetInProgressPath(fileInfo.Path))))
		{
			return true;
		}

		// Only a stray in-progress copy of a finished file can be left to fail here; the failure is logged
		// and the records stay, so the next lookup retries.
		_ = Delete(fileInfo.FileName);
		return false;
	}

	private FileInfo? FindFileInfo(string uniqueFileName) =>
		_dbContext.Read(SelectFileSql, ReadFileInfo, ("$fileName", uniqueFileName)).FirstOrDefault();

	private void InsertFileInfo(FileInfo fileInfo) =>
		_dbContext.Execute(InsertFileSql,
			("$fileName", fileInfo.FileName),
			("$path", fileInfo.Path),
			("$size", fileInfo.Size),
			("$createdAt", fileInfo.CreatedAt.ToUnixTimeSeconds()),
			("$hash", fileInfo.Hash?.ToString()),
			("$finishedAt", fileInfo.FinishedAt?.ToUnixTimeSeconds()));

	private FilePart[] GetFileParts(string uniqueFileName) =>
		[.. _dbContext.Read(SelectFilePartsOfFileSql, ReadFilePart, ("$fileName", uniqueFileName))];

	private void InsertFilePart(FilePart filePart) =>
		_dbContext.Execute(InsertFilePartSql,
			("$fileName", filePart.FileName),
			("$index", filePart.Index),
			("$size", filePart.Size),
			("$writtenBytes", filePart.WrittenBytes),
			("$lastWriteAt", filePart.LastWriteAt.ToUnixTimeSeconds()));

	/// <summary>Reads a row selected with <see cref="FileColumns"/>.</summary>
	private static FileInfo ReadFileInfo(SqliteDataReader reader) => new()
	{
		FileName = reader.GetString(0),
		Path = reader.GetString(1),
		Size = reader.GetInt64(2),
		CreatedAt = DateTimeOffset.FromUnixTimeSeconds(reader.GetInt64(3)),
		Hash = reader.IsDBNull(4) ? null : new Sha256Hash(reader.GetString(4)),
		FinishedAt = reader.IsDBNull(5) ? null : DateTimeOffset.FromUnixTimeSeconds(reader.GetInt64(5)),
	};

	/// <summary>Reads a row selected with <see cref="FilePartColumns"/>.</summary>
	private static FilePart ReadFilePart(SqliteDataReader reader) => new()
	{
		FileName = reader.GetString(0),
		Index = reader.GetInt64(1),
		Size = reader.GetInt64(2),
		WrittenBytes = reader.GetInt64(3),
		LastWriteAt = DateTimeOffset.FromUnixTimeSeconds(reader.GetInt64(4)),
	};

	private static FileStream OpenForRead(string path) =>
		new(path, FileMode.Open, FileAccess.Read, FileShare.None, 4096, FileOptions.Asynchronous);
}
