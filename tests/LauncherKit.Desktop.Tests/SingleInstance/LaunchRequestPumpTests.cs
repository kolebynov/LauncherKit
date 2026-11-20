using System.Diagnostics.CodeAnalysis;
using LauncherKit.Desktop.Services;
using LauncherKit.Desktop.SingleInstance;
using LauncherKit.Infrastructure;
using R3;

namespace LauncherKit.Desktop.Tests.SingleInstance;

[TestFixture]
public sealed class LaunchRequestPumpTests
{
	[Test]
	public void Start_BacklogAdmittedBeforeStartAndDuringScopeReplacement_HandlesInOrderInCurrentScope()
	{
		// Arrange
		var mocker = new AutoMocker(MockBehavior.Strict);
		using var enqueued = new Subject<Unit>();
		var pending = new Queue<LaunchRequest>();
		SetupQueue(mocker, enqueued, pending);
		var dispatcher = new QueuedDispatcher();
		mocker.Use<IUiDispatcher>(dispatcher);
		var initialScope = new AutoMocker(MockBehavior.Strict);
		var replacementScope = new AutoMocker(MockBehavior.Strict);
		var processed = new List<string>();
		replacementScope.GetMock<ILauncherMessageProcessor>()
			.Setup(p => p.Process(OpenFileMessage.Kind, It.IsAny<string>()))
			.Callback<string, string>((_, payload) => processed.Add(payload));
		IServiceProvider current = initialScope;
		mocker.Use(CreateOptions(() => current));
		using var sut = mocker.CreateInstance<LaunchRequestPump>();
		var a = Request("a");
		var b = Request("b");
		pending.Enqueue(a);
		enqueued.OnNext(Unit.Default);
		dispatcher.Drain();

		// Act
		sut.Start();
		pending.Enqueue(b);
		enqueued.OnNext(Unit.Default);
		current = replacementScope;
		dispatcher.Drain();

		// Assert
		Assert.That(processed, Is.EqualTo(new[] { a.Payload, b.Payload }));
		initialScope.GetMock<ILauncherMessageProcessor>().VerifyNoOtherCalls();
	}

	[Test]
	public void Dispose_PendingDrain_LeavesRequestsUnhandled()
	{
		// Arrange
		var mocker = new AutoMocker(MockBehavior.Strict);
		using var enqueued = new Subject<Unit>();
		var pending = new Queue<LaunchRequest>();
		SetupQueue(mocker, enqueued, pending);
		var dispatcher = new QueuedDispatcher();
		mocker.Use<IUiDispatcher>(dispatcher);
		var scope = new AutoMocker(MockBehavior.Strict);
		mocker.Use(CreateOptions(() => scope));
		var sut = mocker.CreateInstance<LaunchRequestPump>();
		sut.Start();
		pending.Enqueue(Request("a"));
		enqueued.OnNext(Unit.Default);

		// Act
		sut.Dispose();
		dispatcher.Drain();

		// Assert
		Assert.That(pending, Has.Count.EqualTo(1));
		scope.GetMock<ILauncherMessageProcessor>().VerifyNoOtherCalls();
	}

	private delegate bool TryDequeueCallback([MaybeNullWhen(false)] out LaunchRequest request);

	private static void SetupQueue(AutoMocker mocker, Subject<Unit> enqueued, Queue<LaunchRequest> pending)
	{
		var queue = mocker.GetMock<ILaunchRequestQueue>();
		queue.Setup(q => q.Enqueued).Returns(enqueued);
		queue.Setup(q => q.TryDequeue(out It.Ref<LaunchRequest>.IsAny!))
			.Returns(new TryDequeueCallback(pending.TryDequeue));
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
