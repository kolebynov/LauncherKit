using System.Text.Json.Serialization;
using Avalonia.Input;

namespace LauncherKit.DevAutomation;

/// <summary>
/// Finds one shown element: every given criterion must match. <see cref="Index"/> picks one of several matches
/// (0-based, in tree order); without it several matches are an error.
/// </summary>
internal record SelectorRequest
{
	/// <summary>An element's ref from any response; it stays with its control (<see cref="ElementRefs"/>).</summary>
	public int? Ref { get; init; }

	/// <summary><c>AutomationProperties.AutomationId</c> or <c>x:Name</c>.</summary>
	public string? Id { get; init; }

	/// <summary>Exact displayed text.</summary>
	public string? Text { get; init; }

	/// <summary>Part of the displayed text.</summary>
	public string? Contains { get; init; }

	/// <summary>Exact tooltip text, for elements without text such as icon buttons.</summary>
	public string? Tip { get; init; }

	/// <summary>Control type name, e.g. <c>Button</c>.</summary>
	public string? Type { get; init; }

	public int? Index { get; init; }

	[JsonIgnore]
	public bool HasCriteria => Ref is not null || Id is not null || Text is not null || Contains is not null
		|| Tip is not null || Type is not null;
}

/// <summary>Pointer action at a selected element's center or at <see cref="X"/>/<see cref="Y"/> of root <see cref="Root"/>.</summary>
internal sealed record PointerRequest : SelectorRequest
{
	public PointerAction Action { get; init; } = PointerAction.Click;

	/// <summary>Root-local coordinate in device-independent pixels (the screenshot's pixels).</summary>
	public double? X { get; init; }

	public double? Y { get; init; }

	/// <summary>Root index from <c>/tree</c>; 0 is the main window.</summary>
	public int Root { get; init; }

	public MouseButton Button { get; init; } = MouseButton.Left;

	public bool Double { get; init; }

	/// <summary>Wheel delta in lines; positive scrolls up.</summary>
	public double Delta { get; init; }

	public RawInputModifierNames Modifiers { get; init; }
}

internal sealed record TextRequest : SelectorRequest
{
	public required string Value { get; init; }
}

/// <summary>Sends <see cref="Key"/> (pressed and released) or types <see cref="Text"/> into the focused element.</summary>
internal sealed record KeyRequest
{
	public Key? Key { get; init; }

	public string? Text { get; init; }

	public RawInputModifierNames Modifiers { get; init; }

	/// <summary>Root index from <c>/tree</c>; 0 is the main window.</summary>
	public int Root { get; init; }
}

internal sealed record WaitRequest : SelectorRequest
{
	/// <summary>Ignored without selector criteria.</summary>
	public WaitState State { get; init; } = WaitState.Present;

	/// <summary>Waits until <see cref="Core.AppTasks.IAppTaskQueue.TaskCount"/> equals this value.</summary>
	public int? TaskCount { get; init; }

	public double Timeout { get; init; } = 30;
}

internal sealed record ScreenshotRequest : SelectorRequest
{
	/// <summary>Pixels per device-independent pixel; 1 makes screenshot pixels equal pointer coordinates.</summary>
	public double Scale { get; init; } = 1;
}

internal enum PointerAction
{
	Click,
	Move,
	Down,
	Up,
	Wheel,
}

internal enum WaitState
{
	Present,
	Absent,
	Enabled,
	Disabled,
}

/// <summary>Keyboard modifiers by name, e.g. <c>"Control, Shift"</c>.</summary>
[Flags]
internal enum RawInputModifierNames
{
	None = 0,
	Alt = 1,
	Control = 2,
	Shift = 4,
	Meta = 8,
}

internal sealed record ErrorResponse(string Error, IReadOnlyList<UiNode>? Candidates = null);

internal sealed record StatusResponse(
	int ProcessId,
	string? Title,
	double Width,
	double Height,
	double RenderScaling,
	int? TaskCount);

internal sealed record TreeResponse(IReadOnlyList<RootNode> Roots);

internal sealed record RootNode(int Index, string Kind, string? Title, Bounds Bounds, IReadOnlyList<UiNode> Children);

internal sealed record FindResponse(IReadOnlyList<UiNode> Matches);

/// <param name="Hit">The visual under the point when the input was sent, which received it.</param>
internal sealed record PointerResponse(int Root, double X, double Y, UiNode? Target, string? Hit);

internal sealed record ElementResponse(UiNode Element);

/// <summary>The element focused after the input, which keys and text go to.</summary>
internal sealed record KeyResponse(string? Focused);

internal sealed record WaitResponse(IReadOnlyList<UiNode> Matches, int? TaskCount);

internal readonly record struct Bounds(double X, double Y, double Width, double Height);

/// <summary>
/// An element of the semantic tree: interactive controls (with the text of their non-interactive content), texts,
/// controls with an id or a tooltip, and views. Layout-only visuals are skipped and their children lifted.
/// </summary>
internal sealed record UiNode
{
	public required int Ref { get; init; }

	public required string Type { get; init; }

	public string? Id { get; init; }

	public string? Text { get; init; }

	public string? Value { get; init; }

	public string? Tip { get; init; }

	public required int Root { get; init; }

	/// <summary>Root-local bounds in device-independent pixels.</summary>
	public required Bounds Bounds { get; init; }

	public bool? Disabled { get; init; }

	/// <summary>Visible but fully transparent (opacity 0 here or in an ancestor), e.g. a hover-only panel.</summary>
	public bool? Transparent { get; init; }

	/// <summary>Set only in a tree requested with hidden elements.</summary>
	public bool? Hidden { get; init; }

	public bool? Checked { get; init; }

	public bool? Selected { get; init; }

	public bool? Expanded { get; init; }

	public bool? Focused { get; init; }

	public IReadOnlyList<UiNode>? Children { get; init; }
}

[JsonSerializable(typeof(SelectorRequest))]
[JsonSerializable(typeof(PointerRequest))]
[JsonSerializable(typeof(TextRequest))]
[JsonSerializable(typeof(KeyRequest))]
[JsonSerializable(typeof(WaitRequest))]
[JsonSerializable(typeof(ScreenshotRequest))]
[JsonSerializable(typeof(ErrorResponse))]
[JsonSerializable(typeof(StatusResponse))]
[JsonSerializable(typeof(TreeResponse))]
[JsonSerializable(typeof(FindResponse))]
[JsonSerializable(typeof(PointerResponse))]
[JsonSerializable(typeof(ElementResponse))]
[JsonSerializable(typeof(KeyResponse))]
[JsonSerializable(typeof(WaitResponse))]
[JsonSourceGenerationOptions(
	PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
	PropertyNameCaseInsensitive = true,
	DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
	UseStringEnumConverter = true,
	UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
internal sealed partial class DevAutomationJsonContext : JsonSerializerContext;
