using LauncherKit.Desktop.SingleInstance;
using LauncherKit.TestSupport;
using R3;

namespace LauncherKit.Desktop.Tests.SingleInstance;

[TestFixture]
public sealed class LaunchRequestQueueTests
{
	[Test]
	public void TryEnqueue_SeveralRequests_DequeuesInOrderAndNotifiesEach()
	{
		// Arrange
		using var queue = new LaunchRequestQueue();
		using var recorder = new ObservableRecorder<Unit>(queue.Enqueued);
		var first = Request();
		var second = Request("b.7z");

		// Act
		queue.TryEnqueue(first);
		queue.TryEnqueue(second);

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(recorder.Values, Has.Count.EqualTo(2));
			Assert.That(queue.TryDequeue(out var a) ? a : null, Is.SameAs(first));
			Assert.That(queue.TryDequeue(out var b) ? b : null, Is.SameAs(second));
			Assert.That(queue.TryDequeue(out _), Is.False);
		});
	}

	[Test]
	public void TryEnqueue_Full_RejectsWithoutNotifying()
	{
		// Arrange
		using var queue = new LaunchRequestQueue();
		for (var i = 0; i < LaunchRequestQueue.Capacity; i++) queue.TryEnqueue(Request());
		using var recorder = new ObservableRecorder<Unit>(queue.Enqueued);

		// Act
		var accepted = queue.TryEnqueue(Request());

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(accepted, Is.False);
			Assert.That(recorder.Values, Is.Empty);
		});
	}

	[Test]
	public void TryEnqueue_OwnerContextThenRelaunchesUpToCapacity_RejectsTheNext()
	{
		// Arrange
		using var queue = new LaunchRequestQueue();
		queue.TryEnqueue(new LaunchContext([], @"C:\work", true));
		for (var i = 1; i < LaunchRequestQueue.Capacity; i++) queue.TryEnqueue(Request());

		// Act
		var accepted = queue.TryEnqueue(Request());

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(accepted, Is.False);
			Assert.That(queue.TryDequeue(out var first) ? first.IsFirstLaunch : (bool?)null, Is.True);
		});
	}

	[Test]
	public void TryEnqueue_Completed_Rejects()
	{
		// Arrange
		using var queue = new LaunchRequestQueue();
		queue.Dispose();

		// Act
		var accepted = queue.TryEnqueue(Request());

		// Assert
		Assert.That(accepted, Is.False);
	}

	private static LaunchContext Request(string path = "a.7z") => new([path], @"C:\work", false);
}
