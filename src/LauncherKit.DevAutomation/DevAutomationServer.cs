using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using LauncherKit.Core.AppTasks;

namespace LauncherKit.DevAutomation;

/// <summary>
/// Local HTTP API that inspects and drives the running launcher for development checks: semantic UI tree, pointer and
/// keyboard input, automation patterns, waits and screenshots. Compiled into the launcher only with
/// <c>-p:EnableDevAutomation=true</c> and started only when <c>DevAutomation:Port</c> is configured
/// (using the consumer’s environment prefix). See <c>docs/dev-automation.md</c>.
/// </summary>
public sealed class DevAutomationServer : IDisposable
{
	private static readonly TimeSpan FrameTimeout = TimeSpan.FromSeconds(1);
	private static readonly TimeSpan WaitPollInterval = TimeSpan.FromMilliseconds(100);
	private static readonly TimeSpan ActionTimeout = TimeSpan.FromSeconds(5);

	private readonly IClassicDesktopStyleApplicationLifetime _desktop;
	private readonly Func<IServiceProvider> _services;
	private readonly ILogger<DevAutomationServer> _logger;
	private readonly HttpListener _listener = new();
	private readonly CancellationTokenSource _stopping = new();
	private readonly List<Popup> _openPopups = [];
	private readonly IDisposable _popupSubscription;
	private readonly InputInjector _input = new();
	private readonly ElementRefs _refs = new();

	private DevAutomationServer(
		int port,
		IClassicDesktopStyleApplicationLifetime desktop,
		Func<IServiceProvider> services,
		ILogger<DevAutomationServer> logger)
	{
		_desktop = desktop;
		_services = services;
		_logger = logger;
		_popupSubscription = Popup.IsOpenProperty.Changed.AddClassHandler<Popup>((popup, _) =>
		{
			_openPopups.Remove(popup);
			if (popup.IsOpen)
			{
				_openPopups.Add(popup);
			}
		});
		_listener.Prefixes.Add($"http://localhost:{port.ToString(CultureInfo.InvariantCulture)}/");
	}

	/// <summary>Starts the server when <c>DevAutomation:Port</c> is configured; otherwise returns <c>null</c>.</summary>
	/// <param name="services">Current scope's services; the scope changes when the app reloads.</param>
	public static DevAutomationServer? Start(
		IConfiguration configuration,
		IClassicDesktopStyleApplicationLifetime desktop,
		Func<IServiceProvider> services,
		ILoggerFactory loggerFactory)
	{
		var logger = loggerFactory.CreateLogger<DevAutomationServer>();
		var portText = configuration["DevAutomation:Port"];
		if (string.IsNullOrEmpty(portText))
		{
			logger.LogInformation("Dev automation is compiled in but disabled: DevAutomation:Port is not set");
			return null;
		}

		if (!int.TryParse(portText, NumberStyles.None, CultureInfo.InvariantCulture, out var port) || port is < 1 or > 65535)
		{
			throw new InvalidOperationException($"DevAutomation:Port '{portText}' is not a valid port.");
		}

		var server = new DevAutomationServer(port, desktop, services, logger);
		server._listener.Start();
		_ = server.Listen();
		logger.LogWarning("Dev automation listens on http://localhost:{Port}/", port);
		return server;
	}

	private async Task Listen()
	{
		while (!_stopping.IsCancellationRequested)
		{
			HttpListenerContext context;
			try
			{
				context = await _listener.GetContextAsync();
			}
			catch (Exception exception) when (exception is HttpListenerException or ObjectDisposedException
				&& _stopping.IsCancellationRequested)
			{
				return;
			}

			_ = Respond(context);
		}
	}

	private async Task Respond(HttpListenerContext context)
	{
		var request = context.Request;
		Response response;
		try
		{
			response = await Handle(request);
		}
		catch (AutomationException exception)
		{
			response = Response.Json(exception.StatusCode, new ErrorResponse(exception.Message, exception.Candidates),
				DevAutomationJsonContext.Default.ErrorResponse);
		}
		catch (JsonException exception)
		{
			response = Response.Json(400, new ErrorResponse($"Invalid request body: {exception.Message}"),
				DevAutomationJsonContext.Default.ErrorResponse);
		}
		catch (Exception exception)
		{
			_logger.LogError(exception, "Dev automation request {Method} {Path} failed", request.HttpMethod,
				request.Url?.AbsolutePath);
			response = Response.Json(500, new ErrorResponse(exception.ToString()), DevAutomationJsonContext.Default.ErrorResponse);
		}

		try
		{
			using var output = context.Response;
			output.StatusCode = response.StatusCode;
			output.ContentType = response.ContentType;
			output.ContentLength64 = response.Body.Length;
			await output.OutputStream.WriteAsync(response.Body, _stopping.Token);
		}
		catch (Exception exception) when (exception is HttpListenerException or ObjectDisposedException
			or OperationCanceledException)
		{
			_logger.LogDebug(exception, "Dev automation response was not sent");
		}
	}

	private async Task<Response> Handle(HttpListenerRequest request)
	{
		var path = request.Url?.AbsolutePath ?? "/";
		return (request.HttpMethod, path) switch
		{
			("GET", "/status") => await OnUiThread(GetStatus),
			("GET", "/tree") => await OnUiThread(() => GetTree(request.QueryString["hidden"] is "1" or "true")),
			("GET", "/screenshot") => await OnUiThread(() => TakeScreenshot(new ScreenshotRequest())),
			("POST", "/find") => await OnUiThread(Find, await Read(request, DevAutomationJsonContext.Default.SelectorRequest)),
			("POST", "/pointer") => await Act(Point, await Read(request, DevAutomationJsonContext.Default.PointerRequest)),
			("POST", "/invoke") => await Act(Invoke, await Read(request, DevAutomationJsonContext.Default.SelectorRequest)),
			("POST", "/text") => await Act(SetText, await Read(request, DevAutomationJsonContext.Default.TextRequest)),
			("POST", "/key") => await OnUiThread(SendKey, await Read(request, DevAutomationJsonContext.Default.KeyRequest)),
			("POST", "/scroll") => await Act(Scroll, await Read(request, DevAutomationJsonContext.Default.SelectorRequest)),
			("POST", "/wait") => await Wait(await Read(request, DevAutomationJsonContext.Default.WaitRequest)),
			("POST", "/screenshot") => await Act(TakeScreenshot,
				await Read(request, DevAutomationJsonContext.Default.ScreenshotRequest)),
			_ => throw new AutomationException(404, $"Unknown endpoint {request.HttpMethod} {path}."),
		};
	}

	private static async Task<T> Read<T>(HttpListenerRequest request, JsonTypeInfo<T> typeInfo)
	{
		// A JSON body cannot be sent cross-origin by a web page without a CORS preflight, which is never answered.
		if (request.ContentType?.StartsWith("application/json", StringComparison.OrdinalIgnoreCase) != true)
		{
			throw new AutomationException(415, "POST requests need a JSON body with Content-Type: application/json.");
		}

		return await JsonSerializer.DeserializeAsync(request.InputStream, typeInfo)
			?? throw new AutomationException(400, "The request body is null.");
	}

	private async Task<Response> OnUiThread(Func<Response> handler)
	{
		await WaitForFrame();
		return await Dispatcher.UIThread.InvokeAsync(handler);
	}

	private async Task<Response> OnUiThread<T>(Func<T, Response> handler, T request)
	{
		await WaitForFrame();
		return await Dispatcher.UIThread.InvokeAsync(() => handler(request));
	}

	/// <summary>
	/// Runs an action on an element, retrying each frame for <see cref="ActionTimeout"/> while it fails only because the
	/// element is not there yet, disabled, not reachable, or still moving (a page transition, an animation), as a user
	/// would wait: the element must be at the same place in two consecutive frames.
	/// </summary>
	private async Task<Response> Act<T>(Func<T, Response> handler, T request)
		where T : SelectorRequest
	{
		var started = Stopwatch.GetTimestamp();
		(Control Control, Bounds Bounds)? previous = null;
		while (true)
		{
			try
			{
				if (request.HasCriteria)
				{
					await WaitForFrame();
					var current = await Dispatcher.UIThread.InvokeAsync(() => Locate(request));
					if (current != previous)
					{
						previous = current;
						throw new AutomationException(409, $"{Describe(current.Control)} keeps moving.", isRetryable: true);
					}
				}

				return await OnUiThread(handler, request);
			}
			catch (AutomationException exception) when (exception.IsRetryable
				&& Stopwatch.GetElapsedTime(started) < ActionTimeout)
			{
			}
		}
	}

	private (Control Control, Bounds Bounds) Locate(SelectorRequest selector)
	{
		var element = Resolve(selector);
		return (element.Control, element.Node.Bounds);
	}

	/// <summary>
	/// Waits until the changes made so far (e.g. by the previous request) are laid out and rendered: input hit testing
	/// uses the last rendered frame, so bounds read before it would point at where the element used to be.
	/// </summary>
	private async Task WaitForFrame()
	{
		var frame = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		await Dispatcher.UIThread.InvokeAsync(() =>
		{
			if (_desktop.MainWindow is { } window)
			{
				// The callback runs before the frame is rendered; the background job runs after it.
				window.RequestAnimationFrame(_ =>
					Dispatcher.UIThread.Post(() => frame.TrySetResult(), DispatcherPriority.Background));
			}
			else
			{
				frame.TrySetResult();
			}
		});

		try
		{
			await frame.Task.WaitAsync(FrameTimeout, _stopping.Token);
		}
		catch (TimeoutException)
		{
			// A minimized window renders no frames; go on with what is laid out.
		}
	}

	private Response GetStatus()
	{
		var window = GetMainWindow();
		var size = window.ClientSize;
		return Response.Json(200,
			new StatusResponse(Environment.ProcessId, window.Title, size.Width, size.Height, window.RenderScaling,
				GetTaskCount()),
			DevAutomationJsonContext.Default.StatusResponse);
	}

	private Response GetTree(bool includeHidden)
	{
		var tree = BuildTree(includeHidden);
		return Response.Json(200, new TreeResponse(tree.Roots), DevAutomationJsonContext.Default.TreeResponse);
	}

	private Response Find(SelectorRequest selector)
	{
		RequireCriteria(selector);
		var matches = BuildTree().Find(selector).Select(x => x.Node with { Children = null }).ToList();
		return Response.Json(200, new FindResponse(matches), DevAutomationJsonContext.Default.FindResponse);
	}

	private Response Point(PointerRequest request)
	{
		var modifiers = InputInjector.ToRawModifiers(request.Modifiers);
		TopLevel root;
		Point point;
		UiTree.Element? element = null;
		if (request.HasCriteria)
		{
			element = Resolve(request);
			root = element.Root;
			var bounds = element.Node.Bounds;
			point = new Point(bounds.X + (bounds.Width / 2), bounds.Y + (bounds.Height / 2));
			if (request.Action is PointerAction.Click or PointerAction.Down)
			{
				EnsureClickable(element, point);
			}
		}
		else
		{
			if (request.X is not { } x || request.Y is not { } y)
			{
				throw new AutomationException(400, "Give either selector criteria or both x and y.");
			}

			var roots = GetRoots();
			if (request.Root < 0 || request.Root >= roots.Count)
			{
				throw new AutomationException(404, $"There is no root {request.Root}; there are {roots.Count}.");
			}

			root = roots[request.Root];
			point = new Point(x, y);
		}

		// Read before the input changes the UI: the visual that gets the input, which may handle it itself (e.g. a
		// selectable text inside a check box takes the press for text selection).
		var hit = root.InputHitTest(point) as Visual;
		Action action = request.Action switch
		{
			PointerAction.Click => () => _input.Click(root, point, request.Button, request.Double, modifiers),
			PointerAction.Move => () => _input.Move(root, point, modifiers),
			PointerAction.Down => () => _input.Press(root, point, request.Button, modifiers),
			PointerAction.Up => () => _input.Release(root, point, request.Button, modifiers),
			PointerAction.Wheel => () => _input.Wheel(root, point, request.Delta, modifiers),
			_ => throw new AutomationException(400, $"Unknown pointer action {request.Action}."),
		};
		action();

		return Response.Json(200,
			new PointerResponse(element?.RootIndex ?? request.Root, point.X, point.Y,
				element is null ? null : element.Node with { Children = null }, hit is null ? null : Describe(hit)),
			DevAutomationJsonContext.Default.PointerResponse);
	}

	/// <summary>
	/// Fails unless a real click at <paramref name="point"/> would reach the element: the hit is the element or its
	/// descendant, or an ancestor that gets the click in its place (e.g. a menu item for its label, which does not take
	/// input itself) while the point is in the element's visible, unclipped area.
	/// </summary>
	private static void EnsureClickable(UiTree.Element element, Point point)
	{
		if (!element.Control.IsEffectivelyEnabled)
		{
			throw new AutomationException(409, $"{Describe(element.Control)} is disabled.", isRetryable: true);
		}

		var hit = element.Root.InputHitTest(point) as Visual;
		var isReached = hit is not null
			&& (hit == element.Control || element.Control.IsVisualAncestorOf(hit)
				|| (hit.IsVisualAncestorOf(element.Control)
					&& element.Control.GetTransformedBounds()?.Clip.Contains(point) == true));
		if (!isReached)
		{
			throw new AutomationException(409,
				string.Create(CultureInfo.InvariantCulture,
					$"A click at ({point.X}, {point.Y}) would reach {(hit is null ? "nothing" : Describe(hit))} instead of ")
				+ $"{Describe(element.Control)}: it is covered, transparent to input, or scrolled out of view (POST /scroll).",
				isRetryable: true);
		}
	}

	private Response Invoke(SelectorRequest selector)
	{
		var element = Resolve(selector);
		var peer = ControlAutomationPeer.CreatePeerForElement(element.Control);
		Action action = peer switch
		{
			IInvokeProvider invokeProvider => invokeProvider.Invoke,
			IToggleProvider toggleProvider => toggleProvider.Toggle,
			ISelectionItemProvider selectionItemProvider => selectionItemProvider.Select,
			IExpandCollapseProvider { ExpandCollapseState: ExpandCollapseState.Expanded } expandCollapseProvider =>
				expandCollapseProvider.Collapse,
			IExpandCollapseProvider expandCollapseProvider => expandCollapseProvider.Expand,
			_ => throw new AutomationException(409,
				$"{Describe(element.Control)} supports no invoke, toggle, selection or expand pattern; click it instead."),
		};
		action();
		return ElementResponse(element);
	}

	private Response SetText(TextRequest request)
	{
		var element = Resolve(request);
		switch (ControlAutomationPeer.CreatePeerForElement(element.Control))
		{
			case IValueProvider { IsReadOnly: false } valueProvider:
				valueProvider.SetValue(request.Value);
				break;
			case IRangeValueProvider { IsReadOnly: false } rangeValueProvider:
				if (!double.TryParse(request.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
				{
					throw new AutomationException(400, $"{Describe(element.Control)} takes a number, not \"{request.Value}\".");
				}

				if (number < rangeValueProvider.Minimum || number > rangeValueProvider.Maximum)
				{
					throw new AutomationException(400, string.Create(CultureInfo.InvariantCulture,
						$"{Describe(element.Control)} takes {rangeValueProvider.Minimum} to {rangeValueProvider.Maximum}, not {number}."));
				}

				rangeValueProvider.SetValue(number);
				break;
			default:
				throw new AutomationException(409, $"{Describe(element.Control)} has no editable value.");
		}

		return ElementResponse(element);
	}

	private Response SendKey(KeyRequest request)
	{
		var roots = GetRoots();
		if (request.Root < 0 || request.Root >= roots.Count)
		{
			throw new AutomationException(404, $"There is no root {request.Root}; there are {roots.Count}.");
		}

		var root = roots[request.Root];
		switch (request)
		{
			case { Key: { } key, Text: null }:
				InputInjector.Key(root, key, InputInjector.ToRawModifiers(request.Modifiers));
				break;
			case { Key: null, Text: { Length: > 0 } text }:
				InputInjector.Type(root, text);
				break;
			default:
				throw new AutomationException(400, "Give either key or a non-empty text.");
		}

		var focused = root.FocusManager?.GetFocusedElement() as Visual;
		return Response.Json(200, new KeyResponse(focused is null ? null : Describe(focused)),
			DevAutomationJsonContext.Default.KeyResponse);
	}

	private Response Scroll(SelectorRequest selector)
	{
		var element = Resolve(selector);
		element.Control.BringIntoView();
		return ElementResponse(element);
	}

	private async Task<Response> Wait(WaitRequest request)
	{
		if (!request.HasCriteria && request.TaskCount is null)
		{
			throw new AutomationException(400, "Give selector criteria, taskCount, or both.");
		}

		using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_stopping.Token);
		timeout.CancelAfter(TimeSpan.FromSeconds(request.Timeout));
		var observed = "nothing";
		while (true)
		{
			var (isDone, matches, taskCount, description) = await Dispatcher.UIThread.InvokeAsync(() => Check(request));
			if (isDone)
			{
				return Response.Json(200, new WaitResponse(matches, taskCount), DevAutomationJsonContext.Default.WaitResponse);
			}

			observed = description;
			try
			{
				await Task.Delay(WaitPollInterval, timeout.Token);
			}
			catch (OperationCanceledException) when (!_stopping.IsCancellationRequested)
			{
				throw new AutomationException(408, $"Timed out after {request.Timeout} s; last observed {observed}.");
			}
		}
	}

	private (bool IsDone, IReadOnlyList<UiNode> Matches, int? TaskCount, string Description) Check(WaitRequest request)
	{
		var taskCount = GetTaskCount();
		var isTaskCountDone = request.TaskCount is null || request.TaskCount == taskCount;
		if (!request.HasCriteria)
		{
			return (isTaskCountDone, [], taskCount, $"taskCount {taskCount?.ToString(CultureInfo.InvariantCulture) ?? "none"}");
		}

		var matches = BuildTree().Find(request).Select(x => x.Node with { Children = null }).ToList();
		var isStateDone = request.State switch
		{
			WaitState.Present => matches.Count > 0,
			WaitState.Absent => matches.Count == 0,
			WaitState.Enabled => matches.Count == 1 && matches[0].Disabled is null,
			WaitState.Disabled => matches.Count == 1 && matches[0].Disabled is true,
			_ => throw new AutomationException(400, $"Unknown state {request.State}."),
		};
		var description = $"{matches.Count} matching element(s)"
			+ (matches.Count == 1 ? matches[0].Disabled is true ? " (disabled)" : " (enabled)" : "")
			+ $", taskCount {taskCount?.ToString(CultureInfo.InvariantCulture) ?? "none"}";
		return (isTaskCountDone && isStateDone, matches, taskCount, description);
	}

	private Response TakeScreenshot(ScreenshotRequest request)
	{
		if (request.Scale is <= 0 or > 4)
		{
			throw new AutomationException(400, "Scale must be in (0, 4].");
		}

		var window = GetMainWindow();
		Rect? crop = null;
		if (request.HasCriteria)
		{
			var control = Resolve(request).Control;
			var origin = window.PointToClient(control.PointToScreen(default));
			crop = new Rect(origin, control.Bounds.Size);
			if (!crop.Value.Intersects(new Rect(window.ClientSize)))
			{
				throw new AutomationException(409,
					$"{Describe(control)} is outside the window; scroll it into view first (POST /scroll).");
			}
		}

		var popups = GetRoots().Where(x => x != window).ToList();
		return new Response(200, "image/png", Screenshot.Capture(window, popups, crop, request.Scale));
	}

	private UiTree.Element Resolve(SelectorRequest selector)
	{
		RequireCriteria(selector);
		var matches = BuildTree().Find(selector);
		if (matches.Count == 0 && selector.Ref is { } reference && _refs.Find(reference) is null)
		{
			throw new AutomationException(410, $"Ref {reference} names no existing element.");
		}

		return matches.Count switch
		{
			0 => throw new AutomationException(404, "No shown element matches the selector.", isRetryable: true),
			1 => matches[0],
			_ => throw new AutomationException(409,
				$"{matches.Count} elements match the selector; add criteria or an index.",
				matches.Select(x => x.Node with { Children = null }).ToList()),
		};
	}

	private UiTree BuildTree(bool includeHidden = false) => UiTree.Build(GetRoots(), _refs, includeHidden);

	private static void RequireCriteria(SelectorRequest selector)
	{
		if (!selector.HasCriteria)
		{
			throw new AutomationException(400, "Give at least one of ref, id, text, contains, tip, type.");
		}
	}

	/// <summary>The main window, the other shown windows, then the open popups in opening order.</summary>
	private List<TopLevel> GetRoots()
	{
		var mainWindow = GetMainWindow();
		var roots = new List<TopLevel> { mainWindow };
		roots.AddRange(_desktop.Windows.Where(x => x != mainWindow && x.IsVisible));
		roots.AddRange(_openPopups.Select(GetPopupHost).OfType<PopupRoot>().Where(x => x.IsVisible));
		return roots;
	}

	// Internal in Avalonia 12, like its IPopupHost type: the popup's window (PopupRoot), or an overlay host already in
	// the window's tree.
	[UnsafeAccessor(UnsafeAccessorKind.Method, Name = "get_Host")]
	[return: UnsafeAccessorType("Avalonia.Controls.Primitives.IPopupHost, Avalonia.Controls")]
	private static extern object? GetPopupHost(Popup popup);

	private Window GetMainWindow() =>
		_desktop.MainWindow ?? throw new AutomationException(503, "The main window is not created yet.");

	private int? GetTaskCount() => _services().GetService<IAppTaskQueue>()?.TaskCount;

	private static Response ElementResponse(UiTree.Element element) =>
		Response.Json(200, new ElementResponse(element.Node with { Children = null }),
			DevAutomationJsonContext.Default.ElementResponse);

	private static string Describe(Visual visual) =>
		visual is Control { Name: { } name } ? $"{visual.GetType().Name} '{name}'" : visual.GetType().Name;

	public void Dispose()
	{
		_stopping.Cancel();
		_listener.Close();
		_popupSubscription.Dispose();
		_input.Dispose();
		_stopping.Dispose();
	}

	private sealed record Response(int StatusCode, string ContentType, byte[] Body)
	{
		public static Response Json<T>(int statusCode, T value, JsonTypeInfo<T> typeInfo) =>
			new(statusCode, "application/json; charset=utf-8", JsonSerializer.SerializeToUtf8Bytes(value, typeInfo));
	}

	/// <summary>A request the API cannot carry out; answered with <see cref="StatusCode"/>, never thrown out of the server.</summary>
	/// <param name="isRetryable">The UI may still get there (element appearing, enabled, done moving).</param>
	[SuppressMessage("Design", "CA1032:Implement standard exception constructors",
		Justification = "Private exception thrown and caught only inside the server, always with a status code.")]
	[SuppressMessage("Design", "CA1064:Exceptions should be public",
		Justification = "Private exception thrown and caught only inside the server, always with a status code.")]
	private sealed class AutomationException(
		int statusCode,
		string message,
		IReadOnlyList<UiNode>? candidates = null,
		bool isRetryable = false)
		: Exception(message)
	{
		public int StatusCode { get; } = statusCode;

		public IReadOnlyList<UiNode>? Candidates { get; } = candidates;

		public bool IsRetryable { get; } = isRetryable;
	}
}
