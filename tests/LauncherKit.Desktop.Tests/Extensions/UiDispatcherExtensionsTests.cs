using R3;
using LauncherKit.Desktop.Extensions;

namespace LauncherKit.Desktop.Tests.Extensions;

[TestFixture]
public sealed class UiDispatcherExtensionsTests
{
	[Test]
	public void ObserveOnUi_QueuesStateChangesAndPreservesOrder()
	{
		using var source = new Subject<int>();
		var dispatcher = new QueuedUiDispatcher();
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
		var dispatcher = new QueuedUiDispatcher();
		var received = new List<int>();
		var subscription = source.ObserveOnUi(dispatcher).Subscribe(received.Add);
		source.OnNext(1);
		subscription.Dispose();
		dispatcher.Drain();
		Assert.That(received, Is.Empty);
	}
}
