using Microsoft.Extensions.Time.Testing;
using LauncherKit.Core.FileSources;
using LauncherKit.Core.UserSettings;
using LauncherKit.TestSupport;

namespace LauncherKit.Core.Tests.FileSources;

[TestFixture]
public sealed class DownloadRateLimiterTests
{
	private const int BucketCapacity = 16 * 1024;
	private const int ReadBufferSize = 64 * 1024;

	[Test]
	public void AcquireAtMost_NoLimitConfigured_CompletesSynchronouslyWithTheFullRequest()
	{
		// Arrange
		var (sut, _, _) = CreateSut(0);

		// Act
		var acquisition = sut.AcquireAtMost(ReadBufferSize, CancellationToken.None);

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(acquisition.IsCompleted, Is.True);
			Assert.That(acquisition.Result, Is.EqualTo(ReadBufferSize));
		});
	}

	[Test]
	public async Task AcquireAtMost_RequestLargerThanTheBucket_GrantsPartiallyAndThrottlesTheNextRequest()
	{
		// Arrange
		var (sut, timeProvider, _) = CreateSut(ReadBufferSize);

		// Act
		var firstGrant = await sut.AcquireAtMost(ReadBufferSize, CancellationToken.None);
		var secondAcquisition = sut.AcquireAtMost(ReadBufferSize, CancellationToken.None).AsTask();
		var completedWithoutTime = secondAcquisition.IsCompleted;
		timeProvider.Advance(TimeSpan.FromSeconds(1));
		var secondGrant = await TaskAssert.WithinTimeout(secondAcquisition);

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(firstGrant, Is.EqualTo(BucketCapacity));
			Assert.That(completedWithoutTime, Is.False, "an empty bucket must not grant instantly");
			Assert.That(firstGrant + secondGrant, Is.LessThanOrEqualTo(ReadBufferSize + BucketCapacity));
		});
	}

	[Test]
	public async Task Refund_UnconsumedGrantReturned_NextRequestSucceedsWithoutWaiting()
	{
		// Arrange
		var (sut, _, _) = CreateSut(ReadBufferSize);
		var grant = await sut.AcquireAtMost(ReadBufferSize, CancellationToken.None);

		// Act
		sut.Refund(grant);
		var secondAcquisition = sut.AcquireAtMost(ReadBufferSize, CancellationToken.None);

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(secondAcquisition.IsCompleted, Is.True);
			Assert.That(secondAcquisition.Result, Is.EqualTo(grant));
		});
	}

	[Test]
	public async Task AcquireAtMost_LimitRemovedWhileWaiting_GrantsTheFullRequest()
	{
		// Arrange
		var (sut, timeProvider, settingsMonitor) = CreateSut(ReadBufferSize);
		await sut.AcquireAtMost(ReadBufferSize, CancellationToken.None);
		var acquisition = sut.AcquireAtMost(ReadBufferSize, CancellationToken.None).AsTask();

		// Act
		settingsMonitor.Change(new DownloadSettings { MaxDownloadSpeedBytesPerSecond = 0 });
		timeProvider.Advance(TimeSpan.FromSeconds(1));

		// Assert
		Assert.That(await TaskAssert.WithinTimeout(acquisition), Is.EqualTo(ReadBufferSize));
	}

	[Test]
	public async Task AcquireAtMost_CancelledWhileWaiting_Throws()
	{
		// Arrange
		var (sut, _, _) = CreateSut(ReadBufferSize);
		await sut.AcquireAtMost(ReadBufferSize, CancellationToken.None);

		// The source is intentionally not disposed: disposing it either races with the wait it cancels
		// (CA2025) or requires ordering that adds nothing to the assertion.
		var cts = new CancellationTokenSource();
		var acquisition = sut.AcquireAtMost(ReadBufferSize, cts.Token).AsTask();

		// Act
		await cts.CancelAsync();

		// Assert
		await Assert.ThrowsAsync<TaskCanceledException>(() => TaskAssert.WithinTimeout(acquisition));
	}

	[Test]
	public async Task AcquireAtMost_LowRateWithSmallClockSteps_GrantsOnceTheAccumulatedBudgetSuffices()
	{
		// Arrange: a rate low enough that a single small step is worth less than one byte.
		var (sut, timeProvider, _) = CreateSut(100);
		await sut.AcquireAtMost(ReadBufferSize, CancellationToken.None);
		var acquisition = sut.AcquireAtMost(100, CancellationToken.None).AsTask();

		// Act
		for (var i = 0; i < 10; i++)
		{
			timeProvider.Advance(TimeSpan.FromMilliseconds(100));
		}

		// Assert
		Assert.That(await TaskAssert.WithinTimeout(acquisition), Is.EqualTo(100));
	}

	private static (DownloadRateLimiter Sut, FakeTimeProvider TimeProvider, OptionsMonitorStub<DownloadSettings> SettingsMonitor)
		CreateSut(long bytesPerSecond)
	{
		var mocker = new AutoMocker(MockBehavior.Strict);
		var settingsMonitor = OptionsStubs.Monitor(
			mocker, new DownloadSettings { MaxDownloadSpeedBytesPerSecond = bytesPerSecond });
		var timeProvider = new FakeTimeProvider();
		mocker.Use<TimeProvider>(timeProvider);
		return (mocker.CreateInstance<DownloadRateLimiter>(enablePrivate: true), timeProvider, settingsMonitor);
	}
}
