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
	public void Run_NoOwner_BecomesOwnerWithoutSendingMessages()
	{
		// Arrange
		var names = UniqueNames();
		var invoked = false;

		// Act
		var result = SingleInstanceElection.Run(names, _ => invoked = true);

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(result.Value, Is.TypeOf<SingleInstanceElection.Result.Owner>());
			Assert.That(invoked, Is.False);
		});
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
		var result = RunOnOtherThread(names, SendOpenFile);

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(result.Value, Is.TypeOf<SingleInstanceElection.Result.Secondary>());
			Assert.That(Kinds(Dequeue(queue, 1)), Is.EqualTo(new[] { OpenFileMessage.Kind }));
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
					var json = SingleInstanceProtocol.Serialize(LaunchRequest.Message(new OpenFileMessage("a.7z"),
						TestMessagesJsonContext.Default.OpenFileMessage));
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
			var result = RunOnOtherThread(names, SendOpenFile);

			// Assert
			Assert.Multiple(() =>
			{
				Assert.That(result.Value, Is.TypeOf<SingleInstanceElection.Result.Secondary>());
				Assert.That(Kinds(Dequeue(queue, 1)), Is.EqualTo(new[] { OpenFileMessage.Kind }));
			});
			owner.ReleaseMutex();
		}
		finally
		{
			lingering?.Dispose();
		}
	}

	[Test]
	public void Run_SecondaryUnderSingleThreadedContext_NeverPostsAndCallsBackOnCallingThread()
	{
		// Arrange
		var names = UniqueNames();
		using var owner = Own(names);
		using var queue = new LaunchRequestQueue();
		using var listener = new SingleInstanceListener(names.Pipe, queue);
		var context = new NonPumpingSynchronizationContext();
		var callingThread = 0;
		var callbackThread = -1;

		// Act
		var result = Task.Run(() =>
		{
			var previous = SynchronizationContext.Current;
			SynchronizationContext.SetSynchronizationContext(context);
			try
			{
				callingThread = Environment.CurrentManagedThreadId;
				return SingleInstanceElection.Run(names, sender =>
				{
					callbackThread = Environment.CurrentManagedThreadId;
					SendOpenFile(sender);
				});
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
			Assert.That(callbackThread, Is.EqualTo(callingThread));
			Assert.That(Kinds(Dequeue(queue, 1)), Is.EqualTo(new[] { OpenFileMessage.Kind }));
		});
		owner.ReleaseMutex();
	}

	[Test]
	public void Run_OwnerListening_DeliversMessagesInOrder()
	{
		// Arrange
		var names = UniqueNames();
		using var owner = Own(names);
		using var queue = new LaunchRequestQueue();
		using var listener = new SingleInstanceListener(names.Pipe, queue);

		// Act
		var result = RunOnOtherThread(names, sender =>
		{
			sender.Send(new OpenFileMessage("a.7z"), TestMessagesJsonContext.Default.OpenFileMessage);
			sender.Send(new OpenFileMessage("b.7z"), TestMessagesJsonContext.Default.OpenFileMessage);
		});
		var requests = Dequeue(queue, 2);

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(result.Value, Is.TypeOf<SingleInstanceElection.Result.Secondary>());
			Assert.That(Kinds(requests), Is.EqualTo(new[] { OpenFileMessage.Kind, OpenFileMessage.Kind }));
			Assert.That(requests.Select(r => OpenFileMessage.PathOf(r.Payload)), Is.EqualTo(new[] { "a.7z", "b.7z" }));
		});
		owner.ReleaseMutex();
	}

	[Test]
	public void Run_NoCallback_SendsNothingAndReturnsSecondary()
	{
		// Arrange
		var names = UniqueNames();
		using var owner = Own(names);
		using var queue = new LaunchRequestQueue();
		using var listener = new SingleInstanceListener(names.Pipe, queue);

		// Act
		var result = RunOnOtherThread(names, null);
		// A later relaunch's message arrives first: the one without a callback sent nothing.
		RunOnOtherThread(names, SendOpenFile);

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(result.Value, Is.TypeOf<SingleInstanceElection.Result.Secondary>());
			Assert.That(Kinds(Dequeue(queue, 1)), Is.EqualTo(new[] { OpenFileMessage.Kind }));
		});
		owner.ReleaseMutex();
	}

	[Test]
	public void Run_OwnerNotListening_ReturnsSecondaryWithoutOwnership()
	{
		// Arrange
		var names = UniqueNames();
		using var owner = Own(names);
		var sent = false;

		// Act
		var result = RunOnOtherThread(names, sender =>
		{
			sender.Send(new OpenFileMessage("a.7z"), TestMessagesJsonContext.Default.OpenFileMessage);
			sent = true;
		});

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(result.Value, Is.TypeOf<SingleInstanceElection.Result.Secondary>());
			Assert.That(sent, Is.True);
		});
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
		var result = RunOnOtherThread(names, SendOpenFile);

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(result.Value, Is.TypeOf<SingleInstanceElection.Result.Secondary>());
			Assert.That(Kinds(Dequeue(queue, 1)), Is.EqualTo(new[] { OpenFileMessage.Kind }));
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
		var result = RunOnOtherThread(names, SendOpenFile);

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(result.Value, Is.TypeOf<SingleInstanceElection.Result.Secondary>());
			Assert.That(Kinds(Dequeue(queue, 1)), Is.EqualTo(new[] { OpenFileMessage.Kind }));
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
		var result = RunOnOtherThread(names, SendOpenFile);

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(result.Value, Is.TypeOf<SingleInstanceElection.Result.Secondary>());
			Assert.That(Kinds(Dequeue(queue, 1)), Is.EqualTo(new[] { OpenFileMessage.Kind }));
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
		var result = RunOnOtherThread(names, SendOpenFile);

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(result.Value, Is.TypeOf<SingleInstanceElection.Result.Secondary>());
			Assert.That(queue.TryDequeue(out _), Is.False);
		});
		owner.ReleaseMutex();
	}

	[Test]
	public void Run_CallbackThrows_DeliversSentMessagesAndReturnsSecondary()
	{
		// Arrange
		var names = UniqueNames();
		using var owner = Own(names);
		using var queue = new LaunchRequestQueue();
		using var listener = new SingleInstanceListener(names.Pipe, queue);

		// Act
		var result = RunOnOtherThread(names, sender =>
		{
			SendOpenFile(sender);
			throw new InvalidOperationException("Consumer failure.");
		});

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(result.Value, Is.TypeOf<SingleInstanceElection.Result.Secondary>());
			Assert.That(Kinds(Dequeue(queue, 1)), Is.EqualTo(new[] { OpenFileMessage.Kind }));
		});
		owner.ReleaseMutex();
	}

	[Test]
	public void Run_OversizedMessage_DeliversEarlierMessagesAndReturnsSecondary()
	{
		// Arrange
		var names = UniqueNames();
		using var owner = Own(names);
		using var queue = new LaunchRequestQueue();
		using var listener = new SingleInstanceListener(names.Pipe, queue);

		// Act
		var result = RunOnOtherThread(names, sender =>
		{
			SendOpenFile(sender);
			sender.Send(new OpenFileMessage(new string('a', SingleInstanceProtocol.MaxRequestBytes)),
				TestMessagesJsonContext.Default.OpenFileMessage);
		});

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(result.Value, Is.TypeOf<SingleInstanceElection.Result.Secondary>());
			var requests = Dequeue(queue, 1);
			Assert.That(Kinds(requests), Is.EqualTo(new[] { OpenFileMessage.Kind }));
			Assert.That(OpenFileMessage.PathOf(requests[0].Payload), Is.EqualTo("a.7z"));
		});
		owner.ReleaseMutex();
	}

	private static void SendOpenFile(ILauncherMessageSender sender) =>
		sender.Send(new OpenFileMessage("a.7z"), TestMessagesJsonContext.Default.OpenFileMessage);

	private static string[] Kinds(IEnumerable<LaunchRequest> requests) => [.. requests.Select(r => r.Kind)];

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
	private static LaunchRequest[] Dequeue(LaunchRequestQueue queue, int count)
	{
		var requests = new List<LaunchRequest>();
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
	private static SingleInstanceElection.Result RunOnOtherThread(SingleInstanceNames names, Action<ILauncherMessageSender>? sendMessages) =>
		Task.Run(() => SingleInstanceElection.Run(names, sendMessages)).GetAwaiter().GetResult();

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
