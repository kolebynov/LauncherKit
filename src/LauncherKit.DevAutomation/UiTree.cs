using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace LauncherKit.DevAutomation;

/// <summary>Snapshot of the semantic tree of every root; built and used on the UI thread.</summary>
internal sealed class UiTree
{
	private readonly List<Element> _elements = [];
	private readonly ElementRefs _refs;
	private readonly bool _includeHidden;

	public IReadOnlyList<RootNode> Roots { get; }

	public IReadOnlyList<Element> Elements => _elements;

	private UiTree(IReadOnlyList<TopLevel> roots, ElementRefs refs, bool includeHidden)
	{
		_refs = refs;
		_includeHidden = includeHidden;
		var rootNodes = new List<RootNode>(roots.Count);
		for (var index = 0; index < roots.Count; index++)
		{
			var root = roots[index];
			var children = new List<UiNode>();
			foreach (var child in root.GetVisualChildren())
			{
				Visit(child, root, index, 1, false, children);
			}

			var size = root.ClientSize;
			rootNodes.Add(new RootNode(
				index,
				root is PopupRoot ? "popup" : "window",
				(root as Window)?.Title,
				new Bounds(0, 0, size.Width, size.Height),
				children));
		}

		Roots = rootNodes;
	}

	public static UiTree Build(IReadOnlyList<TopLevel> roots, ElementRefs refs, bool includeHidden = false) =>
		new(roots, refs, includeHidden);

	/// <summary>Elements matching every criterion of <paramref name="selector"/>, in tree order.</summary>
	public IReadOnlyList<Element> Find(SelectorRequest selector)
	{
		var matches = _elements.Where(x => Matches(x, selector)).ToList();
		if (selector.Index is { } index)
		{
			return index >= 0 && index < matches.Count ? [matches[index]] : [];
		}

		return matches;
	}

	private static bool Matches(Element element, SelectorRequest selector)
	{
		var node = element.Node;
		return (selector.Ref is null || selector.Ref == node.Ref)
			&& (selector.Id is null || selector.Id == node.Id || selector.Id == element.Control.Name)
			&& (selector.Text is null || selector.Text == node.Text)
			&& (selector.Contains is null || node.Text?.Contains(selector.Contains, StringComparison.OrdinalIgnoreCase) == true)
			&& (selector.Tip is null || selector.Tip == node.Tip)
			&& (selector.Type is null || selector.Type == node.Type);
	}

	/// <summary>Adds the nodes for <paramref name="visual"/> and its descendants to <paramref name="output"/>.</summary>
	/// <param name="opacity">Product of the ancestors' opacities.</param>
	/// <param name="textOwned">An interactive ancestor already shows the descendant texts.</param>
	private void Visit(Visual visual, TopLevel root, int rootIndex, double opacity, bool textOwned, List<UiNode> output)
	{
		var isHidden = !visual.IsVisible;
		if (isHidden && !_includeHidden)
		{
			return;
		}

		opacity *= visual.Opacity;
		if (visual is not Control control)
		{
			VisitChildren(visual, root, rootIndex, opacity, textOwned, output);
			return;
		}

		var peer = ControlAutomationPeer.CreatePeerForElement(control);
		var isInteractive = IsInteractive(peer);
		var ownsText = isInteractive && control is not Expander;
		var text = ownsText ? JoinTexts(control) : textOwned ? null : GetOwnText(control);
		var tip = GetTip(control);
		var automationId = AutomationProperties.GetAutomationId(control);
		var isNamed = control.Name is not null && control.TemplatedParent is null;
		if (!isInteractive && text is null && tip is null && automationId is null && !isNamed && control is not UserControl)
		{
			VisitChildren(visual, root, rootIndex, opacity, textOwned, output);
			return;
		}

		var element = new Element(control, root, rootIndex);
		var reference = _refs.GetRef(control);
		_elements.Add(element);

		var children = new List<UiNode>();
		VisitChildren(visual, root, rootIndex, opacity, textOwned || ownsText, children);

		element.Node = new UiNode
		{
			Ref = reference,
			Type = control.GetType().Name,
			Id = automationId ?? (isNamed ? control.Name : null),
			Text = text,
			Value = GetValue(peer),
			Tip = tip,
			Root = rootIndex,
			Bounds = GetBounds(control, root),
			Disabled = control.IsEffectivelyEnabled ? null : true,
			Transparent = opacity == 0 ? true : null,
			Hidden = isHidden ? true : null,
			Checked = (peer as IToggleProvider)?.ToggleState switch
			{
				ToggleState.On => true,
				ToggleState.Off => false,
				_ => null,
			},
			Selected = (peer as ISelectionItemProvider)?.IsSelected,
			Expanded = (peer as IExpandCollapseProvider)?.ExpandCollapseState switch
			{
				ExpandCollapseState.Expanded => true,
				ExpandCollapseState.Collapsed => false,
				_ => null,
			},
			Focused = control.IsFocused ? true : null,
			Children = children.Count == 0 ? null : children,
		};
		output.Add(element.Node);
	}

	private void VisitChildren(Visual visual, TopLevel root, int rootIndex, double opacity, bool textOwned,
		List<UiNode> output)
	{
		foreach (var child in visual.GetVisualChildren())
		{
			Visit(child, root, rootIndex, opacity, textOwned, output);
		}
	}

	private static bool IsInteractive(AutomationPeer peer) =>
		peer is IInvokeProvider or IToggleProvider or IValueProvider or IRangeValueProvider or ISelectionItemProvider
			or IExpandCollapseProvider;

	public static Bounds GetBounds(Visual visual, TopLevel root)
	{
		var origin = visual.TranslatePoint(default, root) ?? default;
		return new Bounds(Round(origin.X), Round(origin.Y), Round(visual.Bounds.Width), Round(visual.Bounds.Height));
	}

	private static double Round(double value) => Math.Round(value, 1);

	/// <summary>The text a control shows itself: a <see cref="TextBlock"/>'s, or the name of a text automation peer
	/// (e.g. the Markdown renderer's <c>CTextBlock</c>).</summary>
	private static string? GetOwnText(Control control) => control switch
	{
		TextBlock textBlock => NullIfBlank(textBlock.Text ?? textBlock.Inlines?.Text),
		_ when ControlAutomationPeer.CreatePeerForElement(control) is var peer
			&& peer.GetAutomationControlType() == AutomationControlType.Text => NullIfBlank(peer.GetName()),
		_ => null,
	};

	/// <summary>Visible texts of the control's subtree, without those of nested interactive controls.</summary>
	private static string? JoinTexts(Control control)
	{
		var texts = new List<string>();
		CollectTexts(control, texts);
		return texts.Count == 0 ? null : string.Join(" ", texts);
	}

	private static void CollectTexts(Visual visual, List<string> texts)
	{
		foreach (var child in visual.GetVisualChildren())
		{
			if (!child.IsVisible || child is Control control && IsInteractive(ControlAutomationPeer.CreatePeerForElement(control)))
			{
				continue;
			}

			if (child is Control childControl && GetOwnText(childControl) is { } text)
			{
				texts.Add(text);
			}

			CollectTexts(child, texts);
		}
	}

	private static string? GetTip(Control control) => ToolTip.GetTip(control) switch
	{
		null => null,
		string text => NullIfBlank(text),
		Control tipControl => JoinTexts(tipControl) ?? GetOwnText(tipControl),
		var tip => NullIfBlank(Convert.ToString(tip, CultureInfo.InvariantCulture)),
	};

	private static string? GetValue(AutomationPeer peer) => peer switch
	{
		IValueProvider valueProvider => valueProvider.Value,
		IRangeValueProvider rangeValueProvider => rangeValueProvider.Value.ToString(CultureInfo.InvariantCulture),
		_ => null,
	};

	private static string? NullIfBlank(string? text) => string.IsNullOrWhiteSpace(text) ? null : text;

	/// <summary>Control behind a node; <see cref="Node"/> is set once its subtree is visited.</summary>
	internal sealed class Element(Control control, TopLevel root, int rootIndex)
	{
		public Control Control { get; } = control;

		public TopLevel Root { get; } = root;

		public int RootIndex { get; } = rootIndex;

		public UiNode Node { get; set; } = null!;
	}
}
