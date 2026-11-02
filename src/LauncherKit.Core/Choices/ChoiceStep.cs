namespace LauncherKit.Core.Choices;

/// <summary>
/// One page of a choice selection: a group of options constrained by <see cref="SelectionMode"/>.
/// Compared by reference, so a step identifies itself in the selection result.
/// </summary>
public sealed class ChoiceStep
{
	public required ChoiceText Title { get; init; }

	public required ChoiceText Subtitle { get; init; }

	public required ChoiceSelectionMode SelectionMode { get; init; }

	public required IReadOnlyList<ChoiceOption> Options { get; init; }
}
