namespace LauncherKit.Core.Choices;

public readonly struct ChoiceStepSelection
{
	public required ChoiceStep Step { get; init; }

	public required IReadOnlyList<ChoiceOption> SelectedOptions { get; init; }
}
