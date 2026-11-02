using Microsoft.Extensions.Options;
using LauncherKit.Core.Extensions;
using LauncherKit.Core.UserSettings;

namespace LauncherKit.Core.FileSources;

/// <summary>
/// Token bucket shared by every HTTP download, so the configured cap applies to the sum of all
/// concurrent parts and files. Bytes are granted before they are read and the unused remainder is
/// refunded, which keeps the amount of in-flight data bounded instead of overshooting by one read
/// buffer per part.
/// </summary>
internal sealed class DownloadRateLimiter : IDownloadRateLimiter
{
	private const int MinGrantBytes = 16 * 1024;

	/// <summary>Windows timer resolution is ~15 ms; shorter waits degenerate into a spin.</summary>
	private static readonly TimeSpan MinDelay = TimeSpan.FromMilliseconds(15);

	private readonly Lock _gate = new();
	private readonly TimeProvider _timeProvider;
	private DownloadSettings _downloadSettings = null!;
	private long _availableBytes;
	private long _lastRefillTimestamp;

	public DownloadRateLimiter(IOptionsMonitor<DownloadSettings> downloadSettings, TimeProvider timeProvider)
	{
		ArgumentNullException.ThrowIfNull(downloadSettings);
		ArgumentNullException.ThrowIfNull(timeProvider);

		_timeProvider = timeProvider;
		downloadSettings.UpdateFieldOnChange(() => ref _downloadSettings!);

		// The bucket starts full: a fresh limiter must not stall the first read. A balance left over
		// from a higher rate is clamped down on the next refill.
		_availableBytes = GetCapacity(_downloadSettings.MaxDownloadSpeedBytesPerSecond);
		_lastRefillTimestamp = _timeProvider.GetTimestamp();
	}

	public ValueTask<int> AcquireAtMost(int maxBytes, CancellationToken cancellationToken)
	{
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxBytes);

		// The unlimited path is the common one and must not allocate.
		return _downloadSettings.MaxDownloadSpeedBytesPerSecond <= 0
			? ValueTask.FromResult(maxBytes)
			: new ValueTask<int>(AcquireThrottled(maxBytes, cancellationToken));
	}

	public void Refund(int bytes)
	{
		if (bytes <= 0)
		{
			return;
		}

		var rate = _downloadSettings.MaxDownloadSpeedBytesPerSecond;
		if (rate <= 0)
		{
			return;
		}

		var capacity = GetCapacity(rate);
		lock (_gate)
		{
			_availableBytes = Math.Min(capacity, _availableBytes + bytes);
		}
	}

	/// <summary>A quarter second of budget: enough for every concurrent part to keep making progress
	/// without letting a resumed download spike.</summary>
	private static long GetCapacity(long rate) => Math.Max(rate / 4, MinGrantBytes);

	private async Task<int> AcquireThrottled(int maxBytes, CancellationToken cancellationToken)
	{
		while (true)
		{
			// Re-read on every iteration, so a changed setting takes effect without rebuilding the limiter.
			var rate = _downloadSettings.MaxDownloadSpeedBytesPerSecond;
			if (rate <= 0)
			{
				return maxBytes;
			}

			TimeSpan wait;
			lock (_gate)
			{
				Refill(rate);

				// Waiters are not queued: a partial grant guarantees that every one of them progresses.
				var minGrant = (int)Math.Min(maxBytes, MinGrantBytes);
				var grant = (int)Math.Min(maxBytes, _availableBytes);
				if (grant >= minGrant)
				{
					_availableBytes -= grant;
					return grant;
				}

				wait = TimeSpan.FromSeconds((minGrant - _availableBytes) / (double)rate);
			}

			await Task.Delay(wait < MinDelay ? MinDelay : wait, _timeProvider, cancellationToken);
		}
	}

	private void Refill(long rate)
	{
		var capacity = GetCapacity(rate);
		if (_availableBytes > capacity)
		{
			_availableBytes = capacity;
		}

		var added = (long)(_timeProvider.GetElapsedTime(_lastRefillTimestamp).TotalSeconds * rate);
		if (added <= 0)
		{
			// Keep the timestamp, so sub-byte fractions accumulate instead of being truncated away on
			// every call, which would drift the effective rate below the configured one.
			return;
		}

		_lastRefillTimestamp = _timeProvider.GetTimestamp();
		_availableBytes = Math.Min(capacity, _availableBytes + added);
	}
}
