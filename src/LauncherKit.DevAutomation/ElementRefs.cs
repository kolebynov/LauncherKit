using System.Runtime.CompilerServices;
using Avalonia.Controls;

namespace LauncherKit.DevAutomation;

/// <summary>
/// Gives every control one ref for the server's lifetime, so a ref from any response (tree, find, wait, an action's
/// element, error candidates) keeps naming the same control. Controls are held weakly. UI thread only.
/// </summary>
internal sealed class ElementRefs
{
	private readonly ConditionalWeakTable<Control, StrongBox<int>> _refs = [];
	private readonly Dictionary<int, WeakReference<Control>> _controls = [];
	private int _next;
	private int _pruneAt = 1024;

	public int GetRef(Control control)
	{
		if (_refs.TryGetValue(control, out var known))
		{
			return known.Value;
		}

		var reference = _next++;
		_refs.Add(control, new StrongBox<int>(reference));
		_controls.Add(reference, new WeakReference<Control>(control));
		if (_controls.Count >= _pruneAt)
		{
			foreach (var (deadRef, _) in _controls.Where(x => !x.Value.TryGetTarget(out _)).ToList())
			{
				_controls.Remove(deadRef);
			}

			_pruneAt = Math.Max(1024, _controls.Count * 2);
		}

		return reference;
	}

	public Control? Find(int reference) =>
		_controls.TryGetValue(reference, out var control) && control.TryGetTarget(out var target) ? target : null;
}
