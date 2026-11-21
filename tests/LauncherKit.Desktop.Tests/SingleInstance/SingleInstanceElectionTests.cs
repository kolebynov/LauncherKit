using System.Buffers.Binary;
using System.IO.Pipes;
using LauncherKit.Desktop.SingleInstance;
using R3;

namespace LauncherKit.Desktop.Tests.SingleInstance;

/// <summary>Real mutex and pipe exchanges under a unique product key per test, so tests never meet each other or a launcher.</summary>
[TestFixture]
public sealed class SingleInstanceElectionTests
{
	private static readonly TimeSpan DeliveryTimeout = TimeSpan.FromSeconds(10);

	[Test]
	public void Run_NoOwner_BecomesOwner()
	{
		// Arrange
		var names = UniqueNames();

		// Act
		var result = SingleInstanceElection.Run(names, ["a.7z"], WorkingDirectory);

		// Assert
		Assert.That(result.Value, Is.TypeOf<SingleInstanceElection.Result.Owner>());
		if (result is SingleInstanceElection.Result.Owner(var mutex))
		{
			mutex.ReleaseMutex();
			mutex.Dispose();
		}
	}

	[Test]
	public void Run_StalledClientHoldsPipePastTimeout_ListenerStillAdmitsRequests()
	{
		// Arrange
		var names = UniqueNames();
		using var owner = Own(names);
		using var queue = new LaunchRequestQueue();
		using var disconnected = new SemaphoreSlim(0);
		var connections = 0;
		// Only the stalled first connection times out at once; the relaunch gets the production timeout.
		using var listener = new SingleInstanceListener(names.Pipe, queue,
			() => connections++ == 0 ? TimeSpan.Zero : SingleInstanceProtocol.ExchangeTimeout, () => disconnected.Release());
		// Holds its handle without writing until the test ends.
		using var stalled = Connect(names);
		Assert.That(disconnected.Wait(DeliveryTimeout), Is.True, "The stalled connection was not disconnected.");

		// Act
		var result = RunOnOtherThread(names, Args);

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(result.Value, Is.TypeOf<SingleInstanceElection.Result.Secondary>());
			Assert.That(Dequeue(queue, 1)[0].Args, Is.EqualTo(Args));
		});
		owner.ReleaseMutex();
	}

	[Test]
	public void Run_PreviousOwnerShutDownWhileClientHoldsPipe_NextOwnerAdmitsRequests()
	{
		// Arrange
		var names = UniqueNames();
		using var previousQueue = new LaunchRequestQueue();
		// Sends one request and keeps its handle open through the restart, so the previous owner shuts down
		// mid-connection. Its connection never times out, so only shutdown can end it.
		NamedPipeClientStream? lingering = null;
		try
		{
			using (var previousOwner = Own(names))
			{
				try
				{
					using var previousListener = new SingleInstanceListener(names.Pipe, previousQueue,
						() => Timeout.InfiniteTimeSpan, null);
					lingering = Connect(names);
					var json = SingleInstanceProtocol.Serialize(new LaunchContext(Args, WorkingDirectory, false));
					lingering.Write(Length(json.Length));
					lingering.Write(json);
					Dequeue(previousQueue, 1);
				}
				finally
				{
					previousOwner.ReleaseMutex();
				}
			}
			using var owner = Own(names);
			using var queue = new LaunchRequestQueue();
			using var listener = new SingleInstanceListener(names.Pipe, queue);

			// Act
			var result = RunOnOtherThread(names, Args);

			// Assert
			Assert.Multiple(() =>
			{
				Assert.That(result.Value, Is.TypeOf<SingleInstanceElection.Result.Secondary>());
				Assert.That(Dequeue(queue, 1)[0].Args, Is.EqualTo(Args));
			});
			owner.ReleaseMutex();
		}
		finally
		{
			lingering?.Dispose();
		}
	}

	[Test]
	public void Run_SecondaryUnderSingleThreadedContext_NeverPosts()
	{
		// Arrange
		var names = UniqueNames();
		using var owner = Own(names);
		using var queue = new LaunchRequestQueue();
		using var listener = new SingleInstanceListener(names.Pipe, queue);
		var context = new NonPumpingSynchronizationContext();

		// Act
		var result = Task.Run(() =>
		{
			var previous = SynchronizationContext.Current;
			SynchronizationContext.SetSynchronizationContext(context);
			try
			{
				return SingleInstanceElection.Run(names, Args, WorkingDirectory);
			}
			finally
			{
				SynchronizationContext.SetSynchronizationContext(previous);
			}
		}).GetAwaiter().GetResult();

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(result.Value, Is.TypeOf<SingleInstanceElection.Result.Secondary>());
			Assert.That(context.Posted, Is.False);
			Assert.That(Dequeue(queue, 1)[0].Args, Is.EqualTo(Args));
		});
		owner.ReleaseMutex();
	}

	[Test]
	public void Run_OwnerNotListening_ReturnsSecondaryWithoutOwnership()
	{
		// Arrange
		var names = UniqueNames();
		using var owner = Own(names);

		// Act
		var result = RunOnOtherThread(names, Args);

		// Assert
		Assert.That(result.Value, Is.TypeOf<SingleInstanceElection.Result.Secondary>());
		owner.ReleaseMutex();
	}

	[Test]
	public void Run_AfterInvalidFrame_ListenerStillAdmitsRequests()
	{
		// Arrange
		var names = UniqueNames();
		using var owner = Own(names);
		using var queue = new LaunchRequestQueue();
		using var listener = new SingleInstanceListener(names.Pipe, queue);
		using (var client = Connect(names))
		{
			var json = "not json"u8.ToArray();
			client.Write(Length(json.Length));
			client.Write(json);
		}

		// Act
		var result = RunOnOtherThread(names, Args);

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(result.Value, Is.TypeOf<SingleInstanceElection.Result.Secondary>());
			Assert.That(Dequeue(queue, 1)[0].Args, Is.EqualTo(Args));
		});
		owner.ReleaseMutex();
	}

	[Test]
	public void Run_AfterOversizedFrame_DropsItAndListenerStillAdmitsRequests()
	{
		// Arrange
		var names = UniqueNames();
		using var owner = Own(names);
		using var queue = new LaunchRequestQueue();
		using var listener = new SingleInstanceListener(names.Pipe, queue);
		using (var client = Connect(names))
			client.Write(Length(SingleInstanceProtocol.MaxRequestBytes + 1));

		// Act
		var result = RunOnOtherThread(names, Args);

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(result.Value, Is.TypeOf<SingleInstanceElection.Result.Secondary>());
			Assert.That(Dequeue(queue, 1)[0].Args, Is.EqualTo(Args));
		});
		owner.ReleaseMutex();
	}

	[Test]
	public void Run_ClientDisconnectsWithoutRequest_ListenerStillAdmitsRequests()
	{
		// Arrange
		var names = UniqueNames();
		using var owner = Own(names);
		using var queue = new LaunchRequestQueue();
		using var listener = new SingleInstanceListener(names.Pipe, queue);
		using (Connect(names))
		{
		}

		// Act
		var result = RunOnOtherThread(names, Args);

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(result.Value, Is.TypeOf<SingleInstanceElection.Result.Secondary>());
			Assert.That(Dequeue(queue, 1)[0].Args, Is.EqualTo(Args));
		});
		owner.ReleaseMutex();
	}

	[Test]
	public void Run_OwnerQueueClosed_DropsRequestsAndReturnsSecondary()
	{
		// Arrange
		var names = UniqueNames();
		using var owner = Own(names);
		using var queue = new LaunchRequestQueue();
		queue.Complete();
		using var listener = new SingleInstanceListener(names.Pipe, queue);

		// Act
		var result = RunOnOtherThread(names, Args);

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(result.Value, Is.TypeOf<SingleInstanceElection.Result.Secondary>());
			Assert.That(queue.TryDequeue(out _), Is.False);
		});
		owner.ReleaseMutex();
	}

	[Test]
	public void Run_OwnerListening_ForwardsOneContextPerLaunchIncludingArgumentless()
	{
		// Arrange
		var names = UniqueNames();
		using var owner = Own(names);
		using var queue = new LaunchRequestQueue();
		using var listener = new SingleInstanceListener(names.Pipe, queue);
		string[] args = ["a.7z", "", @"..\b ""c"".7z", "Моды"];

		// Act
		var first = RunOnOtherThread(names, args, @"C:\first");
		var second = RunOnOtherThread(names, [], @"D:\second");
		var contexts = Dequeue(queue, 2);

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(first.Value, Is.TypeOf<SingleInstanceElection.Result.Secondary>());
			Assert.That(second.Value, Is.TypeOf<SingleInstanceElection.Result.Secondary>());
			Assert.That(contexts[0].Args, Is.EqualTo(args));
			Assert.That(contexts[0].WorkingDirectory, Is.EqualTo(@"C:\first"));
			Assert.That(contexts[0].IsFirstLaunch, Is.False);
			Assert.That(contexts[1].Args, Is.Empty);
			Assert.That(contexts[1].WorkingDirectory, Is.EqualTo(@"D:\second"));
			Assert.That(contexts[1].IsFirstLaunch, Is.False);
			Assert.That(queue.TryDequeue(out _), Is.False);
		});
		owner.ReleaseMutex();
	}

	[Test]
	public void Run_OversizedContext_DropsItAndLaterRelaunchIsAdmitted()
	{
		// Arrange
		var names = UniqueNames();
		using var owner = Own(names);
		using var queue = new LaunchRequestQueue();
		using var listener = new SingleInstanceListener(names.Pipe, queue);

		// Act
		var oversized = RunOnOtherThread(names, [new string('a', SingleInstanceProtocol.MaxRequestBytes)]);
		var result = RunOnOtherThread(names, Args);

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(oversized.Value, Is.TypeOf<SingleInstanceElection.Result.Secondary>());
			Assert.That(result.Value, Is.TypeOf<SingleInstanceElection.Result.Secondary>());
			Assert.That(Dequeue(queue, 1)[0].Args, Is.EqualTo(Args));
			Assert.That(queue.TryDequeue(out _), Is.False);
		});
		owner.ReleaseMutex();
	}

	private const string WorkingDirectory = @"C:\relaunch";

	private static string[] Args => ["a.7z"];

	private static SingleInstanceNames UniqueNames() =>
		SingleInstanceNames.ForCurrentUser($@"HKEY_CURRENT_USER\Software\LauncherKitTests\{Guid.NewGuid():N}");

	private static Mutex Own(SingleInstanceNames names) =>
		new(true, names.Mutex, new NamedWaitHandleOptions { CurrentUserOnly = true, CurrentSessionOnly = true });

	private static NamedPipeClientStream Connect(SingleInstanceNames names)
	{
		var client = new NamedPipeClientStream(".", names.Pipe, PipeDirection.Out, PipeOptions.CurrentUserOnly);
		client.Connect(5000);
		return client;
	}

	private static byte[] Length(int length)
	{
		var bytes = new byte[sizeof(int)];
		BinaryPrimitives.WriteInt32LittleEndian(bytes, length);
		return bytes;
	}

	// Delivery is one-way, so the test waits for the listener to admit the expected number of requests.
	private static LaunchContext[] Dequeue(LaunchRequestQueue queue, int count)
	{
		var requests = new List<LaunchContext>();
		using var admitted = new SemaphoreSlim(0);
		using var subscription = queue.Enqueued.Subscribe(_ => admitted.Release());
		while (requests.Count < count)
		{
			if (queue.TryDequeue(out var request)) requests.Add(request);
			else if (!admitted.Wait(DeliveryTimeout)) throw new AssertionException($"Received {requests.Count} of {count} requests.");
		}

		return [.. requests];
	}

	// A mutex belongs to its thread, so the relaunch runs on another one.
	private static SingleInstanceElection.Result RunOnOtherThread(SingleInstanceNames names, string[] args,
		string workingDirectory = WorkingDirectory) =>
		Task.Run(() => SingleInstanceElection.Run(names, args, workingDirectory)).GetAwaiter().GetResult();

	// Like a UI context whose thread is blocked in Run: anything posted to it would never run, so it only records posts
	// and runs them elsewhere to keep a failing test from hanging.
	private sealed class NonPumpingSynchronizationContext : SynchronizationContext
	{
		public bool Posted { get; private set; }

		public override void Post(SendOrPostCallback d, object? state)
		{
			Posted = true;
			ThreadPool.QueueUserWorkItem(_ => d(state));
		}

		public override void Send(SendOrPostCallback d, object? state)
		{
			Posted = true;
			d(state);
		}
	}
}
