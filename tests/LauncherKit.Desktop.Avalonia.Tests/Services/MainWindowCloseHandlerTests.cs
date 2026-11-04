using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.Threading;
using LauncherKit.Desktop.Avalonia.Services;
using LauncherKit.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using HandleResult = LauncherKit.Desktop.Avalonia.Services.IAppCloseHandler.HandleResult;

namespace LauncherKit.Desktop.Avalonia.Tests.Services;

[NonParallelizable]
[TestFixture]
public sealed class MainWindowCloseHandlerTests
{
	[AvaloniaTest]
	public void Close_EveryHandlerAllows_RunsHandlersInOrderWithWindowAndCloses()
	{
		// Arrange
		var calls = new List<(string Name, Window Window)>();
		var first = new StubCloseHandler(window => { calls.Add(("first", window)); return Allow(); });
		var second = new StubCloseHandler(window => { calls.Add(("second", window)); return Allow(); });
		var window = CreateWindow([first, second], out var closed);

		// Act
		window.Close();
		Dispatcher.UIThread.RunJobs();

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(closed(), Is.True);
			Assert.That(calls, Is.EqualTo(new[] { ("first", window), ("second", window) }));
		});
	}

	[AvaloniaTest]
	public void Close_HandlerKeepsWindowOpen_StopsPipelineAndAllowsRetry()
	{
		// Arrange
		var allowed = false;
		var first = new StubCloseHandler(_ => allowed ? Allow() : new(HandleResult.CannotClose.Instance));
		var second = new StubCloseHandler(_ => Allow());
		var window = CreateWindow([first, second], out var closed);
		window.Close();
		Dispatcher.UIThread.RunJobs();
		Assert.Multiple(() =>
		{
			Assert.That(closed(), Is.False);
			Assert.That(second.CallCount, Is.Zero);
		});

		// Act
		allowed = true;
		window.Close();
		Dispatcher.UIThread.RunJobs();

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(closed(), Is.True);
			Assert.That(second.CallCount, Is.EqualTo(1));
		});
	}

	[AvaloniaTest]
	public void Close_RepeatedWhileHandlersRun_RunsHandlersOnce()
	{
		// Arrange
		var completion = new TaskCompletionSource<HandleResult>();
		var handler = new StubCloseHandler(_ => new(completion.Task));
		var window = CreateWindow([handler], out var closed);
		window.Close();
		Dispatcher.UIThread.RunJobs();

		// Act
		window.Close();
		Dispatcher.UIThread.RunJobs();
		var closedWhileRunning = closed();
		completion.SetResult(HandleResult.CanClose.Instance);
		Dispatcher.UIThread.RunJobs();

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(closedWhileRunning, Is.False);
			Assert.That(closed(), Is.True);
			Assert.That(handler.CallCount, Is.EqualTo(1));
		});
	}

	[AvaloniaTest]
	public void Close_HandlerFails_LaterHandlersStillRunAndCloses()
	{
		// Arrange
		var failed = new StubCloseHandler(_ => throw new InvalidOperationException("failed"));
		var next = new StubCloseHandler(_ => Allow());
		var window = CreateWindow([failed, next], out var closed);

		// Act
		window.Close();
		Dispatcher.UIThread.RunJobs();

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(closed(), Is.True);
			Assert.That(next.CallCount, Is.EqualTo(1));
		});
	}

	private static ValueTask<HandleResult> Allow() => new(HandleResult.CanClose.Instance);

	private static Window CreateWindow(IAppCloseHandler[] handlers, out Func<bool> closed)
	{
		var services = new ServiceCollection();
		foreach (var handler in handlers)
		{
			services.AddSingleton(handler);
		}

		var provider = services.BuildServiceProvider();
		var mocker = new AutoMocker(MockBehavior.Strict);
		mocker.Use<ILogger<MainWindowCloseHandler>>(NullLogger<MainWindowCloseHandler>.Instance);
		mocker.Use(new LauncherKitDesktopOptions
		{
			GetMainWindow = () => null,
			GetLifetime = () => throw new AssertionException("No lifetime expected."),
			GetCurrentServices = () => provider,
			RefreshMainWindow = () => { },
			ReloadViews = () => { },
		});
		var window = new Window();
		var isClosed = false;
		window.Closed += (_, _) => isClosed = true;
		mocker.CreateInstance<MainWindowCloseHandler>().Attach(window);
		window.Show();
		closed = () => isClosed;
		return window;
	}

	private sealed class StubCloseHandler(Func<Window, ValueTask<HandleResult>> handle) : IAppCloseHandler
	{
		public int CallCount { get; private set; }

		public ValueTask<HandleResult> Handle(Window mainWindow)
		{
			CallCount++;
			return handle(mainWindow);
		}
	}
}
