using System.Diagnostics.CodeAnalysis;
using LauncherKit.Desktop.Services;
using LauncherKit.Desktop.SingleInstance;
using LauncherKit.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using R3;

namespace LauncherKit.Desktop.Tests.SingleInstance;

[TestFixture]
public sealed class LaunchRequestPumpTests
{
	[Test]
	public void Start_OwnerContextAndRelaunchesWithSeveralHandlers_CallsHandlersOfEachContextInOrder()
	{
		// Arrange
		var mocker = new AutoMocker(MockBehavior.Strict);
		using var enqueued = new Subject<Unit>();
		var pending = new Queue<LaunchContext>();
		SetupQueue(mocker, enqueued, pending);
		var dispatcher = new QueuedDispatcher();
		mocker.Use<IUiDispatcher>(dispatcher);
		mocker.Use<ILogger<LaunchRequestPump>>(new CapturedLogs<LaunchRequestPump>());
		var calls = new List<string>();
		using var scope = CreateScope(new RecordingHandler("A", calls), new RecordingHandler("B", calls));
		mocker.Use(CreateOptions(() => scope));
		using var sut = mocker.CreateInstance<LaunchRequestPump>();
		var owner = new LaunchContext(["first"], @"C:\owner", true);
		var a = Context("a");
		var empty = new LaunchContext([], @"C:\relaunch", false);
		pending.Enqueue(owner);
		pending.Enqueue(a);

		// Act
		sut.Start();
		pending.Enqueue(empty);
		enqueued.OnNext(Unit.Default);
		dispatcher.Drain();

		// Assert
		Assert.That(calls, Is.EqualTo(new[]
		{
			"A:first", "B:first",
			"A:a", "B:a",
			"A:", "B:",
		}));
	}

	[Test]
	public void Start_NotDrained_InvokesNothing()
	{
		// Arrange
		var mocker = new AutoMocker(MockBehavior.Strict);
		using var enqueued = new Subject<Unit>();
		var pending = new Queue<LaunchContext>();
		SetupQueue(mocker, enqueued, pending);
		mocker.Use<IUiDispatcher>(new QueuedDispatcher());
		mocker.Use<ILogger<LaunchRequestPump>>(new CapturedLogs<LaunchRequestPump>());
		var calls = new List<string>();
		using var scope = CreateScope(new RecordingHandler("A", calls));
		mocker.Use(CreateOptions(() => scope));
		using var sut = mocker.CreateInstance<LaunchRequestPump>();
		pending.Enqueue(Context("a"));

		// Act
		sut.Start();

		// Assert
		Assert.That(calls, Is.Empty);
	}

	[Test]
	public void Start_ScopeReplacedBetweenHandlers_ResolvesEachHandlerFromCurrentScope()
	{
		// Arrange
		var mocker = new AutoMocker(MockBehavior.Strict);
		using var enqueued = new Subject<Unit>();
		var pending = new Queue<LaunchContext>();
		SetupQueue(mocker, enqueued, pending);
		var dispatcher = new QueuedDispatcher();
		mocker.Use<IUiDispatcher>(dispatcher);
		mocker.Use<ILogger<LaunchRequestPump>>(new CapturedLogs<LaunchRequestPump>());
		var calls = new List<string>();
		IServiceProvider? current = null;
		using var secondScope = CreateScope(new RecordingHandler("second-swap", calls), new RecordingHandler("second", calls));
		// The first handler replaces the scope; the context's remaining handler comes from the new scope.
		using var firstScope = CreateScope(
			new RecordingHandler("swap", calls, () => current = secondScope),
			new RecordingHandler("first", calls));
		current = firstScope;
		mocker.Use(CreateOptions(() => current));
		using var sut = mocker.CreateInstance<LaunchRequestPump>();
		pending.Enqueue(Context("a"));
		pending.Enqueue(Context("b"));

		// Act
		sut.Start();
		dispatcher.Drain();

		// Assert
		Assert.That(calls, Is.EqualTo(new[] { "swap:a", "second:a", "second-swap:b", "second:b" }));
	}

	[Test]
	public void Start_ScopeDisposedWhileHandlerPending_CallsRemainingHandlersFromNewScope()
	{
		// Arrange
		var mocker = new AutoMocker(MockBehavior.Strict);
		using var enqueued = new Subject<Unit>();
		var pending = new Queue<LaunchContext>();
		SetupQueue(mocker, enqueued, pending);
		var dispatcher = new QueuedDispatcher();
		mocker.Use<IUiDispatcher>(dispatcher);
		var logs = new CapturedLogs<LaunchRequestPump>();
		mocker.Use<ILogger<LaunchRequestPump>>(logs);
		var calls = new List<string>();
		var gate = new TaskCompletionSource();
		var services = new ServiceCollection();
		var scopeIds = 0;
		services.AddScoped(_ => new ScopeMarker(++scopeIds));
		services.AddScoped<ILaunchHandler>(sp => new ScopeHandler("A", calls, sp.GetRequiredService<ScopeMarker>(), gate.Task));
		services.AddScoped<ILaunchHandler>(sp => new ScopeHandler("B", calls, sp.GetRequiredService<ScopeMarker>(), Task.CompletedTask));
		using var root = services.BuildServiceProvider();
		var firstScope = root.CreateScope();
		using var secondScope = root.CreateScope();
		var current = firstScope.ServiceProvider;
		mocker.Use(CreateOptions(() => current));
		using var sut = mocker.CreateInstance<LaunchRequestPump>();
		pending.Enqueue(Context("a"));
		sut.Start();
		dispatcher.Drain();
		firstScope.Dispose();
		current = secondScope.ServiceProvider;

		// Act
		gate.SetResult();

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(calls, Is.EqualTo(new[] { "A:1:live", "B:2:live" }));
			Assert.That(logs.Entries, Is.Empty);
		});
	}

	[Test]
	public void Start_HandlerResolutionThrows_RethrowsOnDispatcherAndHandlesLaterContexts()
	{
		// Arrange
		var mocker = new AutoMocker(MockBehavior.Strict);
		using var enqueued = new Subject<Unit>();
		var pending = new Queue<LaunchContext>();
		SetupQueue(mocker, enqueued, pending);
		var dispatcher = new QueuedDispatcher();
		mocker.Use<IUiDispatcher>(dispatcher);
		var logs = new CapturedLogs<LaunchRequestPump>();
		mocker.Use<ILogger<LaunchRequestPump>>(logs);
		var calls = new List<string>();
		var failure = new InvalidOperationException("Constructor failure.");
		var attempts = 0;
		var services = new ServiceCollection();
		services.AddTransient<ILaunchHandler>(_ => attempts++ == 0 ? throw failure : new RecordingHandler("A", calls));
		using var scope = services.BuildServiceProvider();
		mocker.Use(CreateOptions(() => scope));
		using var sut = mocker.CreateInstance<LaunchRequestPump>();
		pending.Enqueue(Context("a"));
		pending.Enqueue(Context("b"));
		sut.Start();

		// Act
		var thrown = Assert.Throws<InvalidOperationException>(dispatcher.Drain);

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(thrown, Is.SameAs(failure));
			Assert.That(calls, Is.EqualTo(new[] { "A:b" }));
			Assert.That(pending, Is.Empty);
			Assert.That(logs.Entries, Is.Empty);
		});
	}

	[Test]
	public void Start_HandlerTaskFaults_LogsErrorAndRunsLaterHandlers()
	{
		// Arrange
		var mocker = new AutoMocker(MockBehavior.Strict);
		using var enqueued = new Subject<Unit>();
		var pending = new Queue<LaunchContext>();
		SetupQueue(mocker, enqueued, pending);
		var dispatcher = new QueuedDispatcher();
		mocker.Use<IUiDispatcher>(dispatcher);
		var logs = new CapturedLogs<LaunchRequestPump>();
		mocker.Use<ILogger<LaunchRequestPump>>(logs);
		var calls = new List<string>();
		var failure = new InvalidOperationException("Async failure.");
		var gate = new TaskCompletionSource();
		using var scope = CreateScope(new AsyncHandler("A", calls, _ => gate.Task), new RecordingHandler("B", calls));
		mocker.Use(CreateOptions(() => scope));
		using var sut = mocker.CreateInstance<LaunchRequestPump>();
		pending.Enqueue(Context("a"));
		sut.Start();
		dispatcher.Drain();

		// Act
		gate.SetException(failure);

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(calls, Is.EqualTo(new[] { "A:a", "B:a" }));
			Assert.That(logs.Entries.Select(entry => entry.Exception), Is.EqualTo(new[] { failure }));
		});
	}

	[Test]
	public void Dispose_CancellationCallbackThrows_LogsErrorWithoutThrowing()
	{
		// Arrange
		var mocker = new AutoMocker(MockBehavior.Strict);
		using var enqueued = new Subject<Unit>();
		var pending = new Queue<LaunchContext>();
		SetupQueue(mocker, enqueued, pending);
		var dispatcher = new QueuedDispatcher();
		mocker.Use<IUiDispatcher>(dispatcher);
		var logs = new CapturedLogs<LaunchRequestPump>();
		mocker.Use<ILogger<LaunchRequestPump>>(logs);
		var calls = new List<string>();
		var failure = new InvalidOperationException("Callback failure.");
		using var scope = CreateScope(new AsyncHandler("A", calls, ct =>
		{
			ct.Register(() => throw failure);
			return new TaskCompletionSource().Task;
		}));
		mocker.Use(CreateOptions(() => scope));
		var sut = mocker.CreateInstance<LaunchRequestPump>();
		pending.Enqueue(Context("a"));
		sut.Start();
		dispatcher.Drain();

		// Act
		Assert.DoesNotThrow(sut.Dispose);

		// Assert
		Assert.That(logs.Entries, Has.Count.EqualTo(1));
		Assert.That(((AggregateException)logs.Entries[0].Exception!).InnerExceptions, Is.EqualTo(new[] { failure }));
	}

	[Test]
	public void Start_NoHandlerRegistered_DrainsWithoutLogging()
	{
		// Arrange
		var mocker = new AutoMocker(MockBehavior.Strict);
		using var enqueued = new Subject<Unit>();
		var pending = new Queue<LaunchContext>();
		SetupQueue(mocker, enqueued, pending);
		var dispatcher = new QueuedDispatcher();
		mocker.Use<IUiDispatcher>(dispatcher);
		var logs = new CapturedLogs<LaunchRequestPump>();
		mocker.Use<ILogger<LaunchRequestPump>>(logs);
		using var scope = new ServiceCollection().BuildServiceProvider();
		mocker.Use(CreateOptions(() => scope));
		using var sut = mocker.CreateInstance<LaunchRequestPump>();
		pending.Enqueue(new LaunchContext([], @"C:\owner", true));
		pending.Enqueue(Context("a"));

		// Act
		sut.Start();
		dispatcher.Drain();

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(pending, Is.Empty);
			Assert.That(logs.Entries, Is.Empty);
		});
	}

	[Test]
	public void Start_FailingHandler_LogsErrorAndRunsLaterHandlersAndContexts()
	{
		// Arrange
		var mocker = new AutoMocker(MockBehavior.Strict);
		using var enqueued = new Subject<Unit>();
		var pending = new Queue<LaunchContext>();
		SetupQueue(mocker, enqueued, pending);
		var dispatcher = new QueuedDispatcher();
		mocker.Use<IUiDispatcher>(dispatcher);
		var logs = new CapturedLogs<LaunchRequestPump>();
		mocker.Use<ILogger<LaunchRequestPump>>(logs);
		var calls = new List<string>();
		var failure = new InvalidOperationException("Consumer failure.");
		var failing = Context("secret-path");
		using var scope = CreateScope(
			new RecordingHandler("A", calls, () => { if (calls[^1] == "A:secret-path") throw failure; }),
			new RecordingHandler("B", calls));
		mocker.Use(CreateOptions(() => scope));
		using var sut = mocker.CreateInstance<LaunchRequestPump>();
		pending.Enqueue(failing);
		pending.Enqueue(Context("b"));

		// Act
		sut.Start();
		dispatcher.Drain();

		// Assert
		Assert.That(calls, Is.EqualTo(new[] { "A:secret-path", "B:secret-path", "A:b", "B:b" }));
		Assert.That(logs.Entries, Has.Count.EqualTo(1));
		Assert.Multiple(() =>
		{
			Assert.That(logs.Entries[0].Level, Is.EqualTo(LogLevel.Error));
			Assert.That(logs.Entries[0].Message, Does.Not.Contain("secret"));
			Assert.That(logs.Entries[0].Exception, Is.SameAs(failure));
		});
	}

	[Test]
	public void Dispose_PendingDrain_LeavesRequestsUnhandled()
	{
		// Arrange
		var mocker = new AutoMocker(MockBehavior.Strict);
		using var enqueued = new Subject<Unit>();
		var pending = new Queue<LaunchContext>();
		SetupQueue(mocker, enqueued, pending);
		var dispatcher = new QueuedDispatcher();
		mocker.Use<IUiDispatcher>(dispatcher);
		mocker.Use<ILogger<LaunchRequestPump>>(new CapturedLogs<LaunchRequestPump>());
		var calls = new List<string>();
		using var scope = CreateScope(new RecordingHandler("A", calls));
		mocker.Use(CreateOptions(() => scope));
		var sut = mocker.CreateInstance<LaunchRequestPump>();
		sut.Start();
		pending.Enqueue(Context("a"));
		enqueued.OnNext(Unit.Default);

		// Act
		sut.Dispose();
		dispatcher.Drain();

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(pending, Has.Count.EqualTo(1));
			Assert.That(calls, Is.Empty);
		});
	}

	[Test]
	public void Start_HandlerDisposesPump_SkipsLaterHandlersAndContexts()
	{
		// Arrange
		var mocker = new AutoMocker(MockBehavior.Strict);
		using var enqueued = new Subject<Unit>();
		var pending = new Queue<LaunchContext>();
		SetupQueue(mocker, enqueued, pending);
		var dispatcher = new QueuedDispatcher();
		mocker.Use<IUiDispatcher>(dispatcher);
		mocker.Use<ILogger<LaunchRequestPump>>(new CapturedLogs<LaunchRequestPump>());
		var calls = new List<string>();
		LaunchRequestPump? sut = null;
		using var scope = CreateScope(
			new RecordingHandler("A", calls, () => sut!.Dispose()),
			new RecordingHandler("B", calls));
		mocker.Use(CreateOptions(() => scope));
		sut = mocker.CreateInstance<LaunchRequestPump>();
		pending.Enqueue(Context("a"));
		pending.Enqueue(Context("b"));

		// Act
		sut.Start();
		dispatcher.Drain();

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(calls, Is.EqualTo(new[] { "A:a" }));
			Assert.That(pending, Has.Count.EqualTo(1));
		});
	}

	[Test]
	public void Start_PendingHandler_DefersLaterHandlersAndContextsUntilItCompletes()
	{
		// Arrange
		var mocker = new AutoMocker(MockBehavior.Strict);
		using var enqueued = new Subject<Unit>();
		var pending = new Queue<LaunchContext>();
		SetupQueue(mocker, enqueued, pending);
		var dispatcher = new QueuedDispatcher();
		mocker.Use<IUiDispatcher>(dispatcher);
		mocker.Use<ILogger<LaunchRequestPump>>(new CapturedLogs<LaunchRequestPump>());
		var calls = new List<string>();
		var gate = new TaskCompletionSource();
		using var scope = CreateScope(
			new AsyncHandler("A", calls, _ => calls.Count == 1 ? gate.Task : Task.CompletedTask),
			new RecordingHandler("B", calls));
		mocker.Use(CreateOptions(() => scope));
		using var sut = mocker.CreateInstance<LaunchRequestPump>();
		pending.Enqueue(Context("a"));
		pending.Enqueue(Context("b"));
		sut.Start();
		dispatcher.Drain();
		pending.Enqueue(Context("c"));
		enqueued.OnNext(Unit.Default);
		dispatcher.Drain();
		var beforeCompletion = calls.ToArray();

		// Act
		gate.SetResult();

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(beforeCompletion, Is.EqualTo(new[] { "A:a" }));
			Assert.That(calls, Is.EqualTo(new[] { "A:a", "B:a", "A:b", "B:b", "A:c", "B:c" }));
		});
	}

	[Test]
	public void Dispose_PendingHandler_CancelsItsTokenAndSkipsLaterHandlersAndContextsWithoutLogging()
	{
		// Arrange
		var mocker = new AutoMocker(MockBehavior.Strict);
		using var enqueued = new Subject<Unit>();
		var pending = new Queue<LaunchContext>();
		SetupQueue(mocker, enqueued, pending);
		var dispatcher = new QueuedDispatcher();
		mocker.Use<IUiDispatcher>(dispatcher);
		var logs = new CapturedLogs<LaunchRequestPump>();
		mocker.Use<ILogger<LaunchRequestPump>>(logs);
		var calls = new List<string>();
		var token = CancellationToken.None;
		var gate = new TaskCompletionSource();
		using var scope = CreateScope(
			new AsyncHandler("A", calls, ct =>
			{
				token = ct;
				ct.Register(() => gate.SetCanceled(ct));
				return gate.Task;
			}),
			new RecordingHandler("B", calls));
		mocker.Use(CreateOptions(() => scope));
		var sut = mocker.CreateInstance<LaunchRequestPump>();
		pending.Enqueue(Context("a"));
		pending.Enqueue(Context("b"));
		sut.Start();
		dispatcher.Drain();

		// Act
		sut.Dispose();

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(token.IsCancellationRequested, Is.True);
			Assert.That(calls, Is.EqualTo(new[] { "A:a" }));
			Assert.That(pending, Has.Count.EqualTo(1));
			Assert.That(logs.Entries, Is.Empty);
		});
	}

	private delegate bool TryDequeueCallback([MaybeNullWhen(false)] out LaunchContext context);

	private static void SetupQueue(AutoMocker mocker, Subject<Unit> enqueued, Queue<LaunchContext> pending)
	{
		var queue = mocker.GetMock<ILaunchRequestQueue>();
		queue.Setup(q => q.Enqueued).Returns(enqueued);
		queue.Setup(q => q.TryDequeue(out It.Ref<LaunchContext>.IsAny!))
			.Returns(new TryDequeueCallback(pending.TryDequeue));
	}

	private static LaunchContext Context(string path) => new([path], @"C:\relaunch", false);

	private static ServiceProvider CreateScope(params ILaunchHandler[] handlers)
	{
		var services = new ServiceCollection();
		foreach (var handler in handlers) services.AddSingleton(handler);
		return services.BuildServiceProvider();
	}

	private static LauncherKitDesktopOptions CreateOptions(Func<IServiceProvider> getCurrentServices) => new()
	{
		GetMainWindow = () => null,
		GetLifetime = () => throw new AssertionException("Unexpected lifetime use."),
		GetCurrentServices = getCurrentServices,
		RefreshMainWindow = () => throw new AssertionException("Unexpected refresh."),
		ReloadViews = () => throw new AssertionException("Unexpected reload."),
	};

	/// <summary>Records <c>name:args</c> for each launch, then runs the optional callback.</summary>
	private sealed class RecordingHandler(string name, List<string> calls, Action? then = null) : ILaunchHandler
	{
		public ValueTask Handle(LaunchContext context, CancellationToken cancellationToken)
		{
			calls.Add($"{name}:{string.Join(",", context.Args)}");
			then?.Invoke();
			return ValueTask.CompletedTask;
		}
	}

	/// <summary>Records <c>name:args</c> for each launch and returns the task of the callback.</summary>
	private sealed class AsyncHandler(string name, List<string> calls, Func<CancellationToken, Task> run) : ILaunchHandler
	{
		public ValueTask Handle(LaunchContext context, CancellationToken cancellationToken)
		{
			calls.Add($"{name}:{string.Join(",", context.Args)}");
			return new ValueTask(run(cancellationToken));
		}
	}

	/// <summary>A scoped service recording its scope's number and whether the scope was disposed.</summary>
	private sealed class ScopeMarker(int id) : IDisposable
	{
		public int Id { get; } = id;
		public bool Disposed { get; private set; }
		public void Dispose() => Disposed = true;
	}

	/// <summary>Records <c>name:scope:live|disposed</c> for each launch and returns the given task.</summary>
	private sealed class ScopeHandler(string name, List<string> calls, ScopeMarker scope, Task run) : ILaunchHandler
	{
		public ValueTask Handle(LaunchContext context, CancellationToken cancellationToken)
		{
			calls.Add($"{name}:{scope.Id}:{(scope.Disposed ? "disposed" : "live")}");
			return new ValueTask(run);
		}
	}

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
