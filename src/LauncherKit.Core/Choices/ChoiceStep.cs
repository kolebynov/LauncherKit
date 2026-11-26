namespace LauncherKit.Core.Choices;

/// <summary>
/// One page of a choice selection: a group of options constrained by <see cref="SelectionMode"/>.
/// Compared by reference, so a step identifies itself in the selection result.
/// </summary>
public sealed class ChoiceStep
{
	/// <summary>The heading above the options; none when the dialog title already says what the step asks.</summary>
	public ChoiceText? Title { get; init; }

	/// <summary>The text under the heading; none when the dialog title already says what the step asks.</summary>
	public ChoiceText? Subtitle { get; init; }

	public required ChoiceSelectionMode SelectionMode { get; init; }

	public required IReadOnlyList<ChoiceOption> Options { get; init; }
}
