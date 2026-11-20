using LauncherKit.Desktop.Services;
using LauncherKit.Desktop.SingleInstance;
using LauncherKit.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace LauncherKit.Desktop.Tests.SingleInstance;

[TestFixture]
public sealed class LaunchRequestPumpTests
{
	[Test]
	public void Start_BacklogAdmittedBeforeStartAndDuringScopeReplacement_HandlesInOrderInCurrentScope()
	{
		// Arrange
		using var provider = LauncherMessageProcessorTests.CreateServices();
		var current = provider.CreateScope();
		using var queue = new LaunchRequestQueue();
		var dispatcher = new QueuedDispatcher();
		using var sut = new LaunchRequestPump(queue, dispatcher, CreateOptions(() => current.ServiceProvider));
		queue.TryEnqueue(Request("a"));
		dispatcher.Drain();
		var handled = provider.GetRequiredService<HandledMessages>().Items;
		Assert.That(handled, Is.Empty);

		// Act
		sut.Start();
		queue.TryEnqueue(Request("b"));
		current.Dispose();
		using var replacement = provider.CreateScope();
		current = replacement;
		dispatcher.Drain();

		// Assert
		var marker = replacement.ServiceProvider.GetRequiredService<ScopeMarker>();
		Assert.That(handled, Is.EqualTo(new[] { ("a", marker), ("b", marker) }));
	}

	[Test]
	public void Dispose_PendingDrain_LeavesRequestsUnhandled()
	{
		// Arrange
		using var provider = LauncherMessageProcessorTests.CreateServices();
		using var scope = provider.CreateScope();
		using var queue = new LaunchRequestQueue();
		var dispatcher = new QueuedDispatcher();
		var sut = new LaunchRequestPump(queue, dispatcher, CreateOptions(() => scope.ServiceProvider));
		sut.Start();
		queue.TryEnqueue(Request("a"));

		// Act
		sut.Dispose();
		dispatcher.Drain();

		// Assert
		Assert.That(provider.GetRequiredService<HandledMessages>().Items, Is.Empty);
	}

	private static LaunchRequest Request(string path) =>
		LaunchRequest.Message(new OpenFileMessage(path), TestMessagesJsonContext.Default.OpenFileMessage);

	private static LauncherKitDesktopOptions CreateOptions(Func<IServiceProvider> getCurrentServices) => new()
	{
		GetMainWindow = () => null,
		GetLifetime = () => throw new AssertionException("Unexpected lifetime use."),
		GetCurrentServices = getCurrentServices,
		RefreshMainWindow = () => throw new AssertionException("Unexpected refresh."),
		ReloadViews = () => throw new AssertionException("Unexpected reload."),
	};

	private sealed class QueuedDispatcher : IUiDispatcher
	{
		private readonly Queue<Action> _actions = new();
		public void Post(Action action) => _actions.Enqueue(action);
		public void Drain()
		{
			while (_actions.TryDequeue(out var action)) action();
		}
	}
}
