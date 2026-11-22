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
	public void Initialize_OwnerContextAndRelaunchesWithSeveralHandlers_CallsHandlersOfEachContextInOrder()
	{
		// Arrange
		var mocker = new AutoMocker(MockBehavior.Strict);
		using var enqueued = new Subject<Unit>();
		var pending = new Queue<LaunchContext>();
		SetupQueue(mocker, enqueued, pending);
		var dispatcher = new QueuedUiDispatcher();
		mocker.Use<IUiDispatcher>(dispatcher);
		mocker.Use<ILogger<LaunchRequestPump>>(new CapturedLogs<LaunchRequestPump>());
		var calls = new List<string>();
		using var scope = CreateScope(new RecordingHandler("A", calls), new RecordingHandler("B", calls));
		using var sut = CreatePump(mocker, scope);
		var owner = new LaunchContext(["first"], @"C:\owner", true);
		var a = Context("a");
		var empty = new LaunchContext([], @"C:\relaunch", false);
		pending.Enqueue(owner);
		pending.Enqueue(a);

		// Act
		sut.Initialize();
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
	public void Initialize_NotDrained_InvokesNothing()
	{
		// Arrange
		var mocker = new AutoMocker(MockBehavior.Strict);
		using var enqueued = new Subject<Unit>();
		var pending = new Queue<LaunchContext>();
		SetupQueue(mocker, enqueued, pending);
		mocker.Use<IUiDispatcher>(new QueuedUiDispatcher());
		mocker.Use<ILogger<LaunchRequestPump>>(new CapturedLogs<LaunchRequestPump>());
		var calls = new List<string>();
		using var scope = CreateScope(new RecordingHandler("A", calls));
		using var sut = CreatePump(mocker, scope);
		pending.Enqueue(Context("a"));

		// Act
		sut.Initialize();

		// Assert
		Assert.That(calls, Is.Empty);
	}

	[Test]
	public void Initialize_HandlerTaskFaults_LogsErrorAndRunsLaterHandlers()
	{
		// Arrange
		var mocker = new AutoMocker(MockBehavior.Strict);
		using var enqueued = new Subject<Unit>();
		var pending = new Queue<LaunchContext>();
		SetupQueue(mocker, enqueued, pending);
		var dispatcher = new QueuedUiDispatcher();
		mocker.Use<IUiDispatcher>(dispatcher);
		var logs = new CapturedLogs<LaunchRequestPump>();
		mocker.Use<ILogger<LaunchRequestPump>>(logs);
		var calls = new List<string>();
		var failure = new InvalidOperationException("Async failure.");
		var gate = new TaskCompletionSource();
		using var scope = CreateScope(new AsyncHandler("A", calls, _ => gate.Task), new RecordingHandler("B", calls));
		using var sut = CreatePump(mocker, scope);
		pending.Enqueue(Context("a"));
		sut.Initialize();
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
		var dispatcher = new QueuedUiDispatcher();
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
		var sut = CreatePump(mocker, scope);
		pending.Enqueue(Context("a"));
		sut.Initialize();
		dispatcher.Drain();

		// Act
		Assert.DoesNotThrow(sut.Dispose);

		// Assert
		Assert.That(logs.Entries, Has.Count.EqualTo(1));
		Assert.That(((AggregateException)logs.Entries[0].Exception!).InnerExceptions, Is.EqualTo(new[] { failure }));
	}

	[Test]
	public void Initialize_NoHandlerRegistered_DrainsWithoutLogging()
	{
		// Arrange
		var mocker = new AutoMocker(MockBehavior.Strict);
		using var enqueued = new Subject<Unit>();
		var pending = new Queue<LaunchContext>();
		SetupQueue(mocker, enqueued, pending);
		var dispatcher = new QueuedUiDispatcher();
		mocker.Use<IUiDispatcher>(dispatcher);
		var logs = new CapturedLogs<LaunchRequestPump>();
		mocker.Use<ILogger<LaunchRequestPump>>(logs);
		using var scope = new ServiceCollection().BuildServiceProvider();
		using var sut = CreatePump(mocker, scope);
		pending.Enqueue(new LaunchContext([], @"C:\owner", true));
		pending.Enqueue(Context("a"));

		// Act
		sut.Initialize();
		dispatcher.Drain();

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(pending, Is.Empty);
			Assert.That(logs.Entries, Is.Empty);
		});
	}

	[Test]
	public void Initialize_FailingHandler_LogsErrorAndRunsLaterHandlersAndContexts()
	{
		// Arrange
		var mocker = new AutoMocker(MockBehavior.Strict);
		using var enqueued = new Subject<Unit>();
		var pending = new Queue<LaunchContext>();
		SetupQueue(mocker, enqueued, pending);
		var dispatcher = new QueuedUiDispatcher();
		mocker.Use<IUiDispatcher>(dispatcher);
		var logs = new CapturedLogs<LaunchRequestPump>();
		mocker.Use<ILogger<LaunchRequestPump>>(logs);
		var calls = new List<string>();
		var failure = new InvalidOperationException("Consumer failure.");
		var failing = Context("secret-path");
		using var scope = CreateScope(
			new RecordingHandler("A", calls, () => { if (calls[^1] == "A:secret-path") throw failure; }),
			new RecordingHandler("B", calls));
		using var sut = CreatePump(mocker, scope);
		pending.Enqueue(failing);
		pending.Enqueue(Context("b"));

		// Act
		sut.Initialize();
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
	public void Initialize_HandlerConstructionFails_LogsErrorAndDrainsLaterContexts()
	{
		// Arrange
		var mocker = new AutoMocker(MockBehavior.Strict);
		using var enqueued = new Subject<Unit>();
		var pending = new Queue<LaunchContext>();
		SetupQueue(mocker, enqueued, pending);
		var dispatcher = new QueuedUiDispatcher();
		mocker.Use<IUiDispatcher>(dispatcher);
		var logs = new CapturedLogs<LaunchRequestPump>();
		mocker.Use<ILogger<LaunchRequestPump>>(logs);
		var calls = new List<string>();
		var failure = new InvalidOperationException("Constructor failure.");
		var constructions = 0;
		var services = new ServiceCollection();
		services.AddTransient<ILaunchHandler>(_ => ++constructions == 1 ? throw failure : new RecordingHandler("A", calls));
		using var scope = services.BuildServiceProvider();
		using var sut = CreatePump(mocker, scope);
		pending.Enqueue(Context("secret-path"));
		pending.Enqueue(Context("b"));

		// Act
		sut.Initialize();
		dispatcher.Drain();

		// Assert
		Assert.That(calls, Is.EqualTo(new[] { "A:b" }));
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
		var dispatcher = new QueuedUiDispatcher();
		mocker.Use<IUiDispatcher>(dispatcher);
		mocker.Use<ILogger<LaunchRequestPump>>(new CapturedLogs<LaunchRequestPump>());
		var calls = new List<string>();
		using var scope = CreateScope(new RecordingHandler("A", calls));
		var sut = CreatePump(mocker, scope);
		sut.Initialize();
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
	public void Initialize_HandlerDisposesPump_SkipsLaterHandlersAndContexts()
	{
		// Arrange
		var mocker = new AutoMocker(MockBehavior.Strict);
		using var enqueued = new Subject<Unit>();
		var pending = new Queue<LaunchContext>();
		SetupQueue(mocker, enqueued, pending);
		var dispatcher = new QueuedUiDispatcher();
		mocker.Use<IUiDispatcher>(dispatcher);
		mocker.Use<ILogger<LaunchRequestPump>>(new CapturedLogs<LaunchRequestPump>());
		var calls = new List<string>();
		LaunchRequestPump? sut = null;
		using var scope = CreateScope(
			new RecordingHandler("A", calls, () => sut!.Dispose()),
			new RecordingHandler("B", calls));
		sut = CreatePump(mocker, scope);
		pending.Enqueue(Context("a"));
		pending.Enqueue(Context("b"));

		// Act
		sut.Initialize();
		dispatcher.Drain();

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(calls, Is.EqualTo(new[] { "A:a" }));
			Assert.That(pending, Has.Count.EqualTo(1));
		});
	}

	[Test]
	public void Initialize_PendingHandler_DefersLaterHandlersAndContextsUntilItCompletes()
	{
		// Arrange
		var mocker = new AutoMocker(MockBehavior.Strict);
		using var enqueued = new Subject<Unit>();
		var pending = new Queue<LaunchContext>();
		SetupQueue(mocker, enqueued, pending);
		var dispatcher = new QueuedUiDispatcher();
		mocker.Use<IUiDispatcher>(dispatcher);
		mocker.Use<ILogger<LaunchRequestPump>>(new CapturedLogs<LaunchRequestPump>());
		var calls = new List<string>();
		var gate = new TaskCompletionSource();
		using var scope = CreateScope(
			new AsyncHandler("A", calls, _ => calls.Count == 1 ? gate.Task : Task.CompletedTask),
			new RecordingHandler("B", calls));
		using var sut = CreatePump(mocker, scope);
		pending.Enqueue(Context("a"));
		pending.Enqueue(Context("b"));
		sut.Initialize();
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
		var dispatcher = new QueuedUiDispatcher();
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
		var sut = CreatePump(mocker, scope);
		pending.Enqueue(Context("a"));
		pending.Enqueue(Context("b"));
		sut.Initialize();
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

	[Test]
	public void Initialize_MainWindowNotOpened_DefersContextsUntilItOpens()
	{
		// Arrange
		var mocker = new AutoMocker(MockBehavior.Strict);
		using var enqueued = new Subject<Unit>();
		var pending = new Queue<LaunchContext>();
		SetupQueue(mocker, enqueued, pending);
		var dispatcher = new QueuedUiDispatcher();
		mocker.Use<IUiDispatcher>(dispatcher);
		mocker.Use<ILogger<LaunchRequestPump>>(new CapturedLogs<LaunchRequestPump>());
		using var opened = new CancellationTokenSource();
		SetupMainWindowOpened(mocker, opened.Token);
		var calls = new List<string>();
		using var scope = CreateScope(new RecordingHandler("A", calls));
		using var sut = CreatePump(mocker, scope, mainWindowOpened: false);
		pending.Enqueue(Context("a"));
		sut.Initialize();
		pending.Enqueue(Context("b"));
		enqueued.OnNext(Unit.Default);
		dispatcher.Drain();
		var beforeOpened = calls.ToArray();

		// Act
		opened.Cancel();

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(beforeOpened, Is.Empty);
			Assert.That(calls, Is.EqualTo(new[] { "A:a", "A:b" }));
		});
	}

	[Test]
	public void Dispose_BeforeMainWindowOpened_LeavesRequestsUnhandled()
	{
		// Arrange
		var mocker = new AutoMocker(MockBehavior.Strict);
		using var enqueued = new Subject<Unit>();
		var pending = new Queue<LaunchContext>();
		SetupQueue(mocker, enqueued, pending);
		var dispatcher = new QueuedUiDispatcher();
		mocker.Use<IUiDispatcher>(dispatcher);
		var logs = new CapturedLogs<LaunchRequestPump>();
		mocker.Use<ILogger<LaunchRequestPump>>(logs);
		using var opened = new CancellationTokenSource();
		SetupMainWindowOpened(mocker, opened.Token);
		var calls = new List<string>();
		using var scope = CreateScope(new RecordingHandler("A", calls));
		var sut = CreatePump(mocker, scope, mainWindowOpened: false);
		pending.Enqueue(Context("a"));
		sut.Initialize();
		dispatcher.Drain();

		// Act
		sut.Dispose();
		opened.Cancel();

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(calls, Is.Empty);
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

	private static LaunchRequestPump CreatePump(AutoMocker mocker, IServiceProvider scope, bool mainWindowOpened = true)
	{
		if (mainWindowOpened) SetupMainWindowOpened(mocker, new CancellationToken(true));
		return new LaunchRequestPump(mocker.Get<ILaunchRequestQueue>(), mocker.Get<IUiDispatcher>(),
			mocker.Get<IAvaloniaApplicationLifetime>(), mocker.Get<ILogger<LaunchRequestPump>>(), scope);
	}

	private static void SetupMainWindowOpened(AutoMocker mocker, CancellationToken opened) =>
		mocker.GetMock<IAvaloniaApplicationLifetime>().SetupGet(x => x.MainWindowOpened).Returns(opened);

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
}
