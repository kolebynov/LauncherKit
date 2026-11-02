using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Raw;

namespace LauncherKit.DevAutomation;

/// <summary>
/// Feeds input into a root's platform input callback, the way the OS window does, so it goes through Avalonia's hit
/// testing, pointer-over, capture and focus handling without moving the real cursor or needing the window in front.
/// Uses its own mouse device; the user's real mouse keeps its own pointer state. UI thread only.
/// </summary>
internal sealed class InputInjector : IDisposable
{
	private readonly MouseDevice _mouse = new();
	private TopLevel? _pointerRoot;

	public void Move(TopLevel root, Point point, RawInputModifiers modifiers)
	{
		if (_pointerRoot is not null && _pointerRoot != root && _pointerRoot.PlatformImpl is not null)
		{
			Send(_pointerRoot, new RawPointerEventArgs(_mouse, Timestamp(), GetInputRoot(_pointerRoot),
				RawPointerEventType.LeaveWindow, new Point(-1, -1), modifiers));
		}

		_pointerRoot = root;
		Send(root, new RawPointerEventArgs(_mouse, Timestamp(), GetInputRoot(root), RawPointerEventType.Move, point,
			modifiers));
	}

	/// <param name="modifiers">Keyboard modifiers; the pressed button's flag is added here.</param>
	public void Press(TopLevel root, Point point, MouseButton button, RawInputModifiers modifiers)
	{
		Move(root, point, modifiers);
		Send(root, new RawPointerEventArgs(_mouse, Timestamp(), GetInputRoot(root), GetDownType(button), point,
			modifiers | GetButtonModifier(button)));
	}

	/// <summary>Moves to <paramref name="point"/> with the button still held (a drag when it moved), then releases it.</summary>
	public void Release(TopLevel root, Point point, MouseButton button, RawInputModifiers modifiers)
	{
		Move(root, point, modifiers | GetButtonModifier(button));
		// The modifiers describe the state after the event, so they no longer include the released button.
		Send(root, new RawPointerEventArgs(_mouse, Timestamp(), GetInputRoot(root), GetUpType(button), point, modifiers));
	}

	public void Click(TopLevel root, Point point, MouseButton button, bool isDouble, RawInputModifiers modifiers)
	{
		Press(root, point, button, modifiers);
		Release(root, point, button, modifiers);
		if (isDouble)
		{
			// The second press counts as a double click while the event timestamps stay within the platform's
			// double-click time, which back-to-back events do.
			Press(root, point, button, modifiers);
			Release(root, point, button, modifiers);
		}
	}

	public void Wheel(TopLevel root, Point point, double delta, RawInputModifiers modifiers)
	{
		Move(root, point, modifiers);
		Send(root, new RawMouseWheelEventArgs(_mouse, Timestamp(), GetInputRoot(root), point, new Vector(0, delta),
			modifiers));
	}

	public static void Key(TopLevel root, Key key, RawInputModifiers modifiers)
	{
		var keyboard = GetKeyboard();
		var inputRoot = GetInputRoot(root);
		Send(root, new RawKeyEventArgs(keyboard, Timestamp(), inputRoot, RawKeyEventType.KeyDown, key, modifiers,
			PhysicalKey.None, null, KeyDeviceType.Keyboard));
		Send(root, new RawKeyEventArgs(keyboard, Timestamp(), inputRoot, RawKeyEventType.KeyUp, key, modifiers,
			PhysicalKey.None, null, KeyDeviceType.Keyboard));
	}

	public static void Type(TopLevel root, string text) =>
		Send(root, new RawTextInputEventArgs(GetKeyboard(), Timestamp(), GetInputRoot(root), text));

	public static RawInputModifiers ToRawModifiers(RawInputModifierNames names) =>
		(names.HasFlag(RawInputModifierNames.Alt) ? RawInputModifiers.Alt : RawInputModifiers.None)
		| (names.HasFlag(RawInputModifierNames.Control) ? RawInputModifiers.Control : RawInputModifiers.None)
		| (names.HasFlag(RawInputModifierNames.Shift) ? RawInputModifiers.Shift : RawInputModifiers.None)
		| (names.HasFlag(RawInputModifierNames.Meta) ? RawInputModifiers.Meta : RawInputModifiers.None);

	private static RawInputModifiers GetButtonModifier(MouseButton button) => button switch
	{
		MouseButton.Left => RawInputModifiers.LeftMouseButton,
		MouseButton.Right => RawInputModifiers.RightMouseButton,
		MouseButton.Middle => RawInputModifiers.MiddleMouseButton,
		_ => throw new ArgumentOutOfRangeException(nameof(button), button, "Only left, right and middle buttons are supported."),
	};

	private static RawPointerEventType GetDownType(MouseButton button) => button switch
	{
		MouseButton.Left => RawPointerEventType.LeftButtonDown,
		MouseButton.Right => RawPointerEventType.RightButtonDown,
		MouseButton.Middle => RawPointerEventType.MiddleButtonDown,
		_ => throw new ArgumentOutOfRangeException(nameof(button), button, "Only left, right and middle buttons are supported."),
	};

	private static RawPointerEventType GetUpType(MouseButton button) => button switch
	{
		MouseButton.Left => RawPointerEventType.LeftButtonUp,
		MouseButton.Right => RawPointerEventType.RightButtonUp,
		MouseButton.Middle => RawPointerEventType.MiddleButtonUp,
		_ => throw new ArgumentOutOfRangeException(nameof(button), button, "Only left, right and middle buttons are supported."),
	};

	private static ulong Timestamp() => (ulong)Environment.TickCount64;

	private static void Send(TopLevel root, RawInputEventArgs args)
	{
		var input = root.PlatformImpl?.Input
			?? throw new InvalidOperationException("The root has no platform input; it is closed.");
		input(args);
	}

	private static KeyboardDevice GetKeyboard() =>
		GetKeyboardInstance(null) ?? throw new InvalidOperationException("The platform has no keyboard device.");

	// Internal in Avalonia 12: the input root a top level's platform input is routed to, and the shared keyboard.
	[UnsafeAccessor(UnsafeAccessorKind.Method, Name = "get_InputRoot")]
	private static extern IInputRoot GetInputRoot(TopLevel topLevel);

	[UnsafeAccessor(UnsafeAccessorKind.StaticMethod, Name = "get_Instance")]
	private static extern KeyboardDevice? GetKeyboardInstance(KeyboardDevice? type);

	public void Dispose() => _mouse.Dispose();
}
