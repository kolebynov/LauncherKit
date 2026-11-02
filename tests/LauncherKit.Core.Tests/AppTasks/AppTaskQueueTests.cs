using Microsoft.Extensions.Logging.Abstractions;
using R3;
using LauncherKit.Core.AppTasks;
using LauncherKit.Core.AppTasks.MicroTasks;
using LauncherKit.TestSupport;

namespace LauncherKit.Core.Tests.AppTasks;

[TestFixture]
public sealed class AppTaskQueueTests
{
	[Test]
	public async Task EnqueueAndWaitForCompletion_TaskWithDependency_RunsDependencyBeforeTask()
	{
		// Arrange
		var executionOrder = new List<string>();
		var dependency = CreateTask("dependency", executionOrder);
		var root = CreateTask("root", executionOrder, [dependency]);
		var mocker = new AutoMocker(MockBehavior.Strict);
		mocker.Use<Microsoft.Extensions.Logging.ILogger<AppTaskQueue>>(NullLogger<AppTaskQueue>.Instance);
		mocker.Use(TimeProvider.System);
		var microTasksFactory = mocker.GetMock<IMicroTasksFactory>().Object;
		ServiceScopeFactoryStub.ScopeFactoryFor(mocker, microTasksFactory);
		await using var sut = mocker.CreateInstance<AppTaskQueue>();

		// Act
		await TaskAssert.WithinTimeout(sut.EnqueueAndWaitForCompletion(root, CancellationToken.None));

		// Assert
		Assert.That(executionOrder, Is.EqualTo(new[] { "dependency", "root" }));
	}

	[Test]
	public async Task EnqueueAndWaitForCompletion_SeveralDependencies_RunsThemDepthFirstInListedOrder()
	{
		// Arrange
		var executionOrder = new List<string>();
		var shared = CreateTask("shared", executionOrder);
		var first = CreateTask("first", executionOrder);
		var second = CreateTask("second", executionOrder, [CreateTask("deep", executionOrder, [shared]), shared]);
		var root = CreateTask("root", executionOrder, [first, second, shared]);
		var mocker = new AutoMocker(MockBehavior.Strict);
		mocker.Use<Microsoft.Extensions.Logging.ILogger<AppTaskQueue>>(NullLogger<AppTaskQueue>.Instance);
		mocker.Use(TimeProvider.System);
		var microTasksFactory = mocker.GetMock<IMicroTasksFactory>().Object;
		ServiceScopeFactoryStub.ScopeFactoryFor(mocker, microTasksFactory);
		await using var sut = mocker.CreateInstance<AppTaskQueue>();

		// Act
		await TaskAssert.WithinTimeout(sut.EnqueueAndWaitForCompletion(root, CancellationToken.None));

		// Assert
		Assert.That(executionOrder, Is.EqualTo(new[] { "first", "shared", "deep", "second", "root" }));
	}

	[Test]
	public async Task CancelAppTask_QueuedTask_RemovesDependentsAndDependenciesNoLongerNeeded()
	{
		// Arrange
		var executionOrder = new List<string>();
		var gate = new TaskCompletionSource();
		var blocker = new AppTask("blocker", new AppTaskDescription.Custom("blocker"), _ => [], [], null)
		{
			OnComplete = async _ =>
			{
				await gate.Task;
				executionOrder.Add("blocker");
			},
		};
		var sevenZip = CreateTask("sevenZip", executionOrder);
		var skyrim = CreateTask("skyrim", executionOrder, [sevenZip]);
		var select = CreateTask("select", executionOrder);
		var install = CreateTask("install", executionOrder, [select, skyrim]);
		var mod = CreateTask("mod", executionOrder, [sevenZip]);
		var mocker = new AutoMocker(MockBehavior.Strict);
		mocker.Use<Microsoft.Extensions.Logging.ILogger<AppTaskQueue>>(NullLogger<AppTaskQueue>.Instance);
		mocker.Use(TimeProvider.System);
		var microTasksFactory = mocker.GetMock<IMicroTasksFactory>().Object;
		ServiceScopeFactoryStub.ScopeFactoryFor(mocker, microTasksFactory);
		await using var sut = mocker.CreateInstance<AppTaskQueue>();
		await sut.Enqueue(blocker, CancellationToken.None);
		await sut.Enqueue(install, CancellationToken.None);
		var modCompletion = (await sut.Enqueue(mod, CancellationToken.None)) switch
		{
			AppTaskEnqueueResult.NotEnqueued => Task.CompletedTask,
			AppTaskEnqueueResult.Enqueued(var t) => t,
		};

		// Act
		await sut.CancelAppTask("select", CancellationToken.None);
		gate.SetResult();
		await TaskAssert.WithinTimeout(modCompletion);

		// Assert
		Assert.That(executionOrder, Is.EqualTo(new[] { "blocker", "sevenZip", "mod" }));
	}

	[Test]
	public async Task EnqueueAndWaitForCompletion_TaskBecomesUnneeded_HidesItAtOnceButCompletesItAfterItsDependencies()
	{
		// Arrange
		var executionOrder = new List<string>();
		var removedTasks = new List<string>();
		var optionalRemoved = new TaskCompletionSource();
		var gate = new TaskCompletionSource();
		var isDecided = false;
		var select = new AppTask("select", new AppTaskDescription.Custom("select"), _ => [], [], null)
		{
			OnComplete = _ =>
			{
				isDecided = true;
				executionOrder.Add("select");
				return ValueTask.CompletedTask;
			},
		};
		var install = new AppTask("install", new AppTaskDescription.Custom("install"), _ => [], [select], null)
		{
			OnComplete = async _ =>
			{
				await gate.Task;
				executionOrder.Add("install");
			},
		};
		var optional = CreateTask("optional", executionOrder, [install], () => isDecided);
		var mocker = new AutoMocker(MockBehavior.Strict);
		mocker.Use<Microsoft.Extensions.Logging.ILogger<AppTaskQueue>>(NullLogger<AppTaskQueue>.Instance);
		mocker.Use(TimeProvider.System);
		var microTasksFactory = mocker.GetMock<IMicroTasksFactory>().Object;
		ServiceScopeFactoryStub.ScopeFactoryFor(mocker, microTasksFactory);
		await using var sut = mocker.CreateInstance<AppTaskQueue>();
		using var subscription = SubscribeToRemovedTasks(sut, removedTasks, optionalRemoved);

		// Act
		var optionalCompletion = sut.EnqueueAndWaitForCompletion(optional, CancellationToken.None);
		await TaskAssert.WithinTimeout(optionalRemoved.Task);
		var isCompletedBeforeDependencies = optionalCompletion.IsCompleted;
		gate.SetResult();
		await TaskAssert.WithinTimeout(optionalCompletion);

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(isCompletedBeforeDependencies, Is.False);
			Assert.That(removedTasks, Is.EqualTo(new[] { "optional" }));
			Assert.That(executionOrder, Is.EqualTo(new[] { "select", "install" }));
		});
	}

	[Test]
	public async Task CancelAppTask_DependencyOfUnneededTask_RemovesTasksDependingOnUnneededTask()
	{
		// Arrange
		var executionOrder = new List<string>();
		var removedTasks = new List<string>();
		var optionalRemoved = new TaskCompletionSource();
		var gate = new TaskCompletionSource();
		var isDecided = false;
		var select = new AppTask("select", new AppTaskDescription.Custom("select"), _ => [], [], null)
		{
			OnComplete = _ =>
			{
				isDecided = true;
				executionOrder.Add("select");
				return ValueTask.CompletedTask;
			},
		};
		var blocker = new AppTask("blocker", new AppTaskDescription.Custom("blocker"), _ => [], [], null)
		{
			OnComplete = async _ =>
			{
				await gate.Task;
				executionOrder.Add("blocker");
			},
		};
		var voices = CreateTask("voices", executionOrder);
		var optional = CreateTask("optional", executionOrder, [select, blocker, voices], () => isDecided);
		var game = CreateTask("game", executionOrder, [optional]);
		var mocker = new AutoMocker(MockBehavior.Strict);
		mocker.Use<Microsoft.Extensions.Logging.ILogger<AppTaskQueue>>(NullLogger<AppTaskQueue>.Instance);
		mocker.Use(TimeProvider.System);
		var microTasksFactory = mocker.GetMock<IMicroTasksFactory>().Object;
		ServiceScopeFactoryStub.ScopeFactoryFor(mocker, microTasksFactory);
		await using var sut = mocker.CreateInstance<AppTaskQueue>();
		using var subscription = SubscribeToRemovedTasks(sut, removedTasks, optionalRemoved);
		await sut.Enqueue(game, CancellationToken.None);
		await TaskAssert.WithinTimeout(optionalRemoved.Task);

		// Act
		await sut.CancelAppTask("voices", CancellationToken.None);
		var lastCompletion = sut.EnqueueAndWaitForCompletion(CreateTask("last", executionOrder), CancellationToken.None);
		gate.SetResult();
		await TaskAssert.WithinTimeout(lastCompletion);

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(removedTasks, Is.EqualTo(new[] { "optional", "voices", "game" }));
			Assert.That(executionOrder, Is.EqualTo(new[] { "select", "blocker", "last" }));
		});
	}

	[Test]
	public async Task TaskCount_TasksCompleteOrBecomeUnneeded_NotifiesVisibleTaskCountUntilQueueIsEmpty()
	{
		// Arrange
		var executionOrder = new List<string>();
		var isDecided = false;
		var select = new AppTask("select", new AppTaskDescription.Custom("select"), _ => [], [], null)
		{
			OnComplete = _ =>
			{
				isDecided = true;
				return ValueTask.CompletedTask;
			},
		};
		var install = CreateTask("install", executionOrder, [select]);
		var optional = CreateTask("optional", executionOrder, [install], () => isDecided);
		var mocker = new AutoMocker(MockBehavior.Strict);
		mocker.Use<Microsoft.Extensions.Logging.ILogger<AppTaskQueue>>(NullLogger<AppTaskQueue>.Instance);
		mocker.Use(TimeProvider.System);
		var microTasksFactory = mocker.GetMock<IMicroTasksFactory>().Object;
		ServiceScopeFactoryStub.ScopeFactoryFor(mocker, microTasksFactory);
		await using var sut = mocker.CreateInstance<AppTaskQueue>();
		var taskCounts = new List<int>();
		sut.PropertyChanged += (_, args) =>
		{
			if (args.PropertyName == nameof(AppTaskQueue.TaskCount))
			{
				taskCounts.Add(sut.TaskCount);
			}
		};

		// Act
		await TaskAssert.WithinTimeout(sut.EnqueueAndWaitForCompletion(optional, CancellationToken.None));

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(taskCounts, Is.EqualTo(new[] { 3, 1, 0 }));
			Assert.That(sut.TaskCount, Is.Zero);
		});
	}

	private static IDisposable SubscribeToRemovedTasks(AppTaskQueue sut, List<string> removedTasks,
		TaskCompletionSource firstRemoved) =>
		sut.Events.Subscribe(evt =>
		{
			if (evt is AppTaskQueue.Event.TaskRemoved(var task, _))
			{
				removedTasks.Add(task.Id);
				firstRemoved.TrySetResult();
			}
		});

	private static AppTask CreateTask(string id, List<string> executionOrder, IReadOnlyList<AppTask>? dependencies = null,
		Func<bool>? isUnneeded = null) =>
		new(id, new AppTaskDescription.Custom(id), _ => [], dependencies ?? [], null)
		{
			IsUnneeded = isUnneeded,
			OnComplete = _ =>
			{
				executionOrder.Add(id);
				return ValueTask.CompletedTask;
			},
		};
}
