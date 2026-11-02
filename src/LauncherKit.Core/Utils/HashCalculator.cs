using System.Buffers;
using System.Security.Cryptography;
using LauncherKit.Core.Models;

namespace LauncherKit.Core.Utils;

public sealed class HashCalculator : IHashCalculator
{
	private const int BufferSize = 4 * 1024 * 1024;

	public async Task<Sha256Hash> HashFile(
		string filePath, Func<double, ValueTask>? progressTracker, CancellationToken cancellationToken)
	{
		using var incrementalHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

		{
			await using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read,
				BufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
			var totalBytes = fileStream.Length;
			var hashedBytes = 0L;

			var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
			try
			{
				var bufferMemory = buffer.AsMemory(0, BufferSize);
				int bytesRead;
				var prevProgress = 0.0;
				while ((bytesRead = await fileStream.ReadAsync(bufferMemory, cancellationToken)) > 0)
				{
					incrementalHash.AppendData(buffer, 0, bytesRead);
					hashedBytes += bytesRead;

					if (hashedBytes >= totalBytes)
					{
						continue;
					}

					var progress = (double)hashedBytes / totalBytes;
					if (progressTracker != null && progress - prevProgress >= 0.01)
					{
						prevProgress = progress;
						await progressTracker(progress);
					}
				}
			}
			finally
			{
				ArrayPool<byte>.Shared.Return(buffer);
			}
		}

		if (progressTracker != null)
		{
			await progressTracker(1.0);
		}

		return new Sha256Hash(incrementalHash.GetHashAndReset());
	}
}
