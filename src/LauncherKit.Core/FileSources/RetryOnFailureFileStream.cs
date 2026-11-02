using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;
using LauncherKit.Core.Extensions;

namespace LauncherKit.Core.FileSources;

internal sealed class RetryOnFailureFileStream : IFileStream
{
	private const string OperationTokenKey = "OperationToken";

	private readonly ILogger<RetryOnFailureFileStream> _logger;
	private readonly AsyncRetryPolicy<int> _retryPolicy;
	private readonly CancellationTokenSource _disposeCts = new();
	private IFileStream _innerStream;
	private long _consumedBytes;

	/// <summary>Bytes consumed since the first stream was opened, across every replacement stream.</summary>
	public long Position => _consumedBytes + _innerStream.Position;

	public RetryOnFailureFileStream(
		IFileStream stream, Func<long, CancellationToken, Task<IFileStream>> newStreamProvider,
		ILogger<RetryOnFailureFileStream> logger, PolicyBuilder<int> policyBuilder, TimeProvider timeProvider)
	{
		_innerStream = stream;
		_logger = logger;
		_retryPolicy = policyBuilder
			.WaitAndRetryAsync(5, _ => TimeSpan.Zero, async (result, _, attempt, context) =>
			{
				_logger.LogWarning(result.Exception, "Reading file stream failed, retrying...");
				using var linkedCts = CreateLinkedCts(context);
				await Task.Delay(TimeSpan.FromMilliseconds(300 * attempt), timeProvider, linkedCts.Token);
				var offset = Position;
				await _innerStream.DisposeAsync();
				_consumedBytes = offset;
				_innerStream = await newStreamProvider(offset, linkedCts.Token);
			});
	}

	public ValueTask<int> Read(Memory<byte> buffer, CancellationToken cancellationToken)
	{
		var context = new Context { [OperationTokenKey] = cancellationToken };
		var task = _retryPolicy.ExecuteAsync(
			(_, ct) => _innerStream.Read(buffer, ct).AsTask(), context, cancellationToken);
		return task.AsValueTask();
	}

	public ValueTask DisposeAsync()
	{
		_disposeCts.Cancel();
		_disposeCts.Dispose();
		return _innerStream.DisposeAsync();
	}

	private CancellationTokenSource CreateLinkedCts(Context context) =>
		context.TryGetValue(OperationTokenKey, out var value) && value is CancellationToken operationToken &&
		operationToken.CanBeCanceled
			? CancellationTokenSource.CreateLinkedTokenSource(_disposeCts.Token, operationToken)
			: CancellationTokenSource.CreateLinkedTokenSource(_disposeCts.Token);
}