using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using LauncherKit.TestSupport;

namespace LauncherKit.Bootstrapper.Tests;

[TestFixture]
public sealed class InterprocessLockTests
{
	private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(500);

	[Test]
	public async Task TryAcquire_EmptyDirectory_CreatesLockFile()
	{
		// Arrange
		using var temp = new TempDirectory();
		var sut = CreateSut(new FakeTimeProvider());

		// Act
		using var acquired = await TaskAssert.WithinTimeout(sut.TryAcquire(temp.Path, CancellationToken.None));

		// Assert
		Assert.That(acquired, Is.Not.Null);
		Assert.That(File.Exists(temp.Combine(".lock")), Is.True);
	}

	[Test]
	public async Task Dispose_AfterAcquire_AllowsReacquiringSameName()
	{
		// Arrange
		using var temp = new TempDirectory();
		var timeProvider = new FakeTimeProvider();
		var sut = CreateSut(timeProvider);
		var first = await TaskAssert.WithinTimeout(sut.TryAcquire(temp.Path, CancellationToken.None));

		// Act
		first!.Dispose();

		// Assert
		var reacquisition = sut.TryAcquire(temp.Path, CancellationToken.None);
		await TaskAssert.Eventually(
			() =>
			{
				timeProvider.Advance(RetryDelay);
				return reacquisition.IsCompleted;
			},
			"reacquisition after release");

		using var second = await reacquisition;
		Assert.That(second, Is.Not.Null);
		Assert.That(File.Exists(temp.Combine(".lock")), Is.True);
	}

	[Test]
	public async Task TryAcquire_WhileHeld_LeavesLockFileExclusivelyLocked()
	{
		// Arrange
		using var temp = new TempDirectory();
		var sut = CreateSut(new FakeTimeProvider());
		var lockFilePath = temp.Combine(".lock");

		// Act
		using var acquired = await TaskAssert.WithinTimeout(sut.TryAcquire(temp.Path, CancellationToken.None));

		// Assert
		Assert.That(acquired, Is.Not.Null);
		Assert.That(
			() => new FileStream(lockFilePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None),
			Throws.InstanceOf<IOException>());
	}

	[Test]
	public async Task TryAcquire_WhileHeld_WaitsUntilTheHolderReleases()
	{
		// Arrange
		using var temp = new TempDirectory();
		var timeProvider = new FakeTimeProvider();
		var sut = CreateSut(timeProvider);
		var first = await TaskAssert.WithinTimeout(sut.TryAcquire(temp.Path, CancellationToken.None));

		// Act
		var second = sut.TryAcquire(temp.Path, CancellationToken.None);
		for (var i = 0; i < 5; i++)
		{
			timeProvider.Advance(RetryDelay);
			await Task.Yield();
		}

		// Assert
		Assert.That(second.IsCompleted, Is.False, "the lock must stay exclusive while the first holder owns it");

		first!.Dispose();
		await TaskAssert.Eventually(
			() =>
			{
				timeProvider.Advance(RetryDelay);
				return second.IsCompleted;
			},
			"second acquisition after release");

		using var acquired = await second;
		Assert.That(acquired, Is.Not.Null);
		Assert.That(File.Exists(temp.Combine(".lock")), Is.True);
	}

	[Test]
	public async Task TryAcquire_CancelledWhileWaiting_ReturnsNull()
	{
		// Arrange
		using var temp = new TempDirectory();
		using var cancellation = new CancellationTokenSource();
		var sut = CreateSut(new FakeTimeProvider());
		using var first = await TaskAssert.WithinTimeout(sut.TryAcquire(temp.Path, CancellationToken.None));

		// Act
		var second = sut.TryAcquire(temp.Path, cancellation.Token);
		await cancellation.CancelAsync();

		// Assert
		Assert.That(await TaskAssert.WithinTimeout(second), Is.Null);
		Assert.That(File.Exists(temp.Combine(".lock")), Is.True, "the holder keeps the lock file");
	}

	private static InterprocessLockFactory CreateSut(FakeTimeProvider timeProvider) =>
		new(timeProvider, NullLogger<InterprocessLockFactory>.Instance);
}
