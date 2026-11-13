using LauncherKit.Core.Choices;

namespace LauncherKit.Core.Feed.Install;

/// <summary>One page of a <see cref="InstallAction.SelectChoices"/> action.</summary>
public sealed class InstallChoiceStep
{
	public required LocalizedText Title { get; init; }

	/// <summary>Text under the title; <see langword="null"/> when missing.</summary>
	public LocalizedText? Subtitle { get; init; }

	public required ChoiceSelectionMode SelectionMode { get; init; }

	public required IReadOnlyList<InstallChoiceOption> Options { get; init; }
}

/// <summary>An option of an <see cref="InstallChoiceStep"/>.</summary>
public sealed class InstallChoiceOption
{
	/// <summary>
	/// The context key under which the action records whether the option was selected, unique among the keys of its
	/// install step.
	/// </summary>
	public required string Id { get; init; }

	public required LocalizedText Name { get; init; }

	/// <summary>Text under the name; <see langword="null"/> when missing.</summary>
	public LocalizedText? Description { get; init; }

	/// <summary>Always selected; the user cannot deselect it. Optional, <see langword="false"/> when missing.</summary>
	public bool IsRequired { get; init; }

	/// <summary>Selected when the page opens; the user may deselect it. Optional, <see langword="false"/> when missing.</summary>
	public bool IsSelectedByDefault { get; init; }
}
