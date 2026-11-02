using System.Buffers;

namespace LauncherKit.Core.FileSources;

public static class FileStreamExtensions
{
	extension(IFileStream fileStream)
	{
		public async Task CopyTo(Stream destination, CancellationToken cancellationToken)
		{
			var buffer = ArrayPool<byte>.Shared.Rent(4 * 1024 * 1024);
			var bufferMemory = buffer.AsMemory();

			try
			{
				int read;
				while ((read = await fileStream.Read(bufferMemory, cancellationToken)) > 0)
				{
					await destination.WriteAsync(bufferMemory[..read], cancellationToken);
				}
			}
			finally
			{
				ArrayPool<byte>.Shared.Return(buffer);
			}
		}
	}
}