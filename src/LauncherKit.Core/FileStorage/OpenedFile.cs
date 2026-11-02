using System.Buffers;
using System.Diagnostics;
using Microsoft.Win32.SafeHandles;
using LauncherKit.Core.Db;

namespace LauncherKit.Core.FileStorage;

/// <summary>
/// Write access to the in-progress copy of a downloaded file. The file is written through positional
/// writes, so every part can be written concurrently without a shared file cursor.
/// </summary>
public sealed class OpenedFile : IAsyncDisposable
{
	private readonly SafeFileHandle _fileHandle;
	private readonly FilePartWriter[] _filePartWriters;
	private bool _isDisposed;

	/// <summary>Path of the in-progress file, not of the published download.</summary>
	public string FullPath { get; }

	public long WrittenBytes => _filePartWriters.Sum(x => x.WrittenBytes);

	public long Size { get; }

	public IEnumerable<FilePartInfo> FileParts => _filePartWriters.Select(x => new FilePartInfo(x.Offset, x.WrittenBytes, x.Size, x.Index));

	public int PartCount => _filePartWriters.Length;

	internal OpenedFile(string inProgressPath, IReadOnlyCollection<FilePart> fileParts, SqliteDbContext dbContext,
		TimeProvider timeProvider)
	{
		if (fileParts.Count == 0)
		{
			throw new ArgumentException("File must have at least one part", nameof(fileParts));
		}

		FullPath = inProgressPath;
		Size = fileParts.Sum(x => x.Size);
		var fileName = fileParts.First().FileName;

		_fileHandle = File.OpenHandle(inProgressPath, FileMode.Open, FileAccess.Write, FileShare.None,
			FileOptions.Asynchronous);
		if (RandomAccess.GetLength(_fileHandle) != Size)
		{
			RandomAccess.SetLength(_fileHandle, Size);
		}

		var databaseLock = new Lock();
		var offset = 0L;
		_filePartWriters = fileParts
			.OrderBy(x => x.Index)
			.Select(part =>
			{
				var filePartWriter = new FilePartWriter(databaseLock, _fileHandle, dbContext, timeProvider,
					fileName, offset, part.WrittenBytes, part.Size, part.Index);
				offset += part.Size;
				return filePartWriter;
			})
			.ToArray();
	}

	/// <remarks>
	/// Each part has a single writer that owns its buffer and its written-bytes counter, so concurrent
	/// calls are safe only for distinct <paramref name="partIndex"/> values.
	/// </remarks>
	public ValueTask WriteAsync(long partIndex, ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken)
	{
		ObjectDisposedException.ThrowIf(_isDisposed, this);

		if (partIndex < 0 || partIndex >= _filePartWriters.Length)
		{
			throw new ArgumentOutOfRangeException(nameof(partIndex), partIndex, $"Part index must be in range [0, {_filePartWriters.Length - 1}])");
		}

		return _filePartWriters[partIndex].WriteAsync(buffer, cancellationToken);
	}

	public async ValueTask DisposeAsync()
	{
		if (_isDisposed)
		{
			return;
		}

		_isDisposed = true;

		foreach (var filePartWriter in _filePartWriters)
		{
			await filePartWriter.DisposeAsync();
		}

		_fileHandle.Dispose();
	}

	private sealed class FilePartWriter : IAsyncDisposable
	{
		private const int FlushBlockSize = 4 * 1024 * 1024;

		private const string UpdateProgressSql =
			$"""
			 UPDATE "{DbConstants.FilePartsTableName}" SET "WrittenBytes" = $writtenBytes, "LastWriteAt" = $lastWriteAt
			 WHERE "FileName" = $fileName AND "Index" = $index
			 """;

		private readonly Lock _databaseLock;
		private readonly SafeFileHandle _fileHandle;
		private readonly SqliteDbContext _dbContext;
		private readonly TimeProvider _timeProvider;
		private readonly string _fileName;
		private byte[]? _buffer;
		private int _currentFlushBlockOffset;

		public long Offset { get; }

		public long WrittenBytes { get; private set; }

		public long Size { get; }

		public long Index { get; }

		public FilePartWriter(Lock databaseLock, SafeFileHandle fileHandle, SqliteDbContext dbContext,
			TimeProvider timeProvider, string fileName, long offset, long writtenBytes, long size, long index)
		{
			_databaseLock = databaseLock;
			_fileHandle = fileHandle;
			_dbContext = dbContext;
			_timeProvider = timeProvider;
			_fileName = fileName;
			Offset = offset;
			WrittenBytes = writtenBytes;
			Size = size;
			Index = index;
			_buffer = ArrayPool<byte>.Shared.Rent(FlushBlockSize);
		}

		public async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
		{
			ObjectDisposedException.ThrowIf(_buffer == null, this);

			if (WrittenBytes + _currentFlushBlockOffset + buffer.Length > Size)
			{
				throw new InvalidOperationException($"Trying to write more bytes than file part size. Part size: {Size}, written bytes: {WrittenBytes}, current flush block offset: {_currentFlushBlockOffset}, buffer length: {buffer.Length}");
			}

			var toCopyTotal = buffer.Length;
			var copied = 0;
			while (toCopyTotal > 0)
			{
				var toCopy = Math.Min(toCopyTotal, _buffer.Length - _currentFlushBlockOffset);
				buffer.Span.Slice(copied, toCopy).CopyTo(_buffer.AsSpan(_currentFlushBlockOffset));
				_currentFlushBlockOffset += toCopy;
				copied += toCopy;
				toCopyTotal -= toCopy;

				Debug.Assert(_currentFlushBlockOffset <= _buffer.Length, "Buffer overflow");

				if (_currentFlushBlockOffset == _buffer.Length)
				{
					await FlushAsync(cancellationToken);
				}
			}
		}

		public async ValueTask DisposeAsync()
		{
			var buffer = _buffer;
			if (buffer == null)
			{
				return;
			}

			try
			{
				await FlushAsync(CancellationToken.None);
			}
			finally
			{
				_buffer = null;
				ArrayPool<byte>.Shared.Return(buffer);
			}
		}

		private async Task FlushAsync(CancellationToken cancellationToken)
		{
			if (_currentFlushBlockOffset == 0 || _buffer == null)
			{
				return;
			}

			await RandomAccess.WriteAsync(_fileHandle, _buffer.AsMemory(0, _currentFlushBlockOffset),
				Offset + WrittenBytes, cancellationToken);

			WrittenBytes += _currentFlushBlockOffset;
			_currentFlushBlockOffset = 0;

			using var _ = _databaseLock.EnterScope();
			_dbContext.Execute(UpdateProgressSql,
				("$writtenBytes", WrittenBytes),
				("$lastWriteAt", _timeProvider.GetUtcNow().ToUnixTimeSeconds()),
				("$fileName", _fileName),
				("$index", Index));
		}
	}

	public readonly record struct FilePartInfo(long Offset, long WrittenBytes, long Size, long Index);
}
