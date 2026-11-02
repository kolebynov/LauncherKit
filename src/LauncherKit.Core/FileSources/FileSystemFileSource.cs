using LauncherKit.Core.Models;
using LauncherKit.Core.Utils;

namespace LauncherKit.Core.FileSources;

public sealed class FileSystemFileSource : IFileSource
{
	private readonly string _filePath;
	private readonly IHashCalculator _hashCalculator;

	public FileSystemFileSource(string filePath, IHashCalculator hashCalculator)
	{
		_filePath = filePath;
		_hashCalculator = hashCalculator;
	}

	public ValueTask<long> GetFileSize(CancellationToken cancellationToken) =>
		ValueTask.FromResult(new FileInfo(_filePath).Length);

	public ValueTask<bool> IsOffsetSupported(CancellationToken cancellationToken) => ValueTask.FromResult(true);

	public async ValueTask<Sha256Hash?> GetHash(CancellationToken cancellationToken) =>
		await _hashCalculator.HashFile(_filePath, null, cancellationToken);

	public ValueTask<IFileStream> OpenStream(long? fromInclusive, long? toInclusive, CancellationToken cancellationToken)
	{
#pragma warning disable CA2000
		var fileStream = new FileStream(_filePath, FileMode.Open, FileAccess.Read, FileShare.Read,
			4 * 1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
		return ValueTask.FromResult<IFileStream>(new FileSystemFileStream(fileStream, fromInclusive, toInclusive));
#pragma warning restore CA2000
	}
}
