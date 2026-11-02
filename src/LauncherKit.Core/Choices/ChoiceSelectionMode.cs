namespace LauncherKit.Core.Choices;

/// <summary>How many options of a <see cref="ChoiceStep"/> the user must select.</summary>
public enum ChoiceSelectionMode
{
	Any,
	AtLeastOne,
	AtMostOne,
	ExactlyOne,
	All,
}
