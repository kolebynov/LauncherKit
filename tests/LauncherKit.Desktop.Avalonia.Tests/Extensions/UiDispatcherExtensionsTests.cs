using R3;
using LauncherKit.Desktop.Extensions;
using LauncherKit.Desktop.Services;

namespace LauncherKit.Desktop.Avalonia.Tests.Extensions;

[TestFixture]
public sealed class UiDispatcherExtensionsTests
{
	[Test]
	public void ObserveOnUi_QueuesStateChangesAndPreservesOrder()
	{
		using var source = new Subject<int>();
		var dispatcher = new QueuedDispatcher();
		var received = new List<int>();
		using var subscription = source.ObserveOnUi(dispatcher).Subscribe(received.Add);
		source.OnNext(1);
		source.OnNext(2);
		Assert.That(received, Is.Empty);
		dispatcher.Drain();
		Assert.That(received, Is.EqualTo(new[] { 1, 2 }));
	}

	[Test]
	public void ObserveOnUi_DisposedSubscriptionDoesNotDeliverQueuedStateChanges()
	{
		using var source = new Subject<int>();
		var dispatcher = new QueuedDispatcher();
		var received = new List<int>();
		var subscription = source.ObserveOnUi(dispatcher).Subscribe(received.Add);
		source.OnNext(1);
		subscription.Dispose();
		dispatcher.Drain();
		Assert.That(received, Is.Empty);
	}

	private sealed class QueuedDispatcher : IUiDispatcher
	{
		private readonly Queue<Action> _queue = new();
		public void Post(Action action) => _queue.Enqueue(action);
		public void Drain()
		{
			while (_queue.TryDequeue(out var action)) action();
		}
	}
}
