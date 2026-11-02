using LauncherKit.Core.Models;

namespace LauncherKit.Core.FileSources;

public interface IFileSource
{
	ValueTask<long> GetFileSize(CancellationToken cancellationToken);

	ValueTask<bool> IsOffsetSupported(CancellationToken cancellationToken);

	ValueTask<Sha256Hash?> GetHash(CancellationToken cancellationToken);

	ValueTask<IFileStream> OpenStream(long? fromInclusive, long? toInclusive, CancellationToken cancellationToken);
}