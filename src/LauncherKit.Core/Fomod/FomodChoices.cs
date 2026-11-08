using FomodInstaller.Scripting.XmlScript;
using LauncherKit.Core.Choices;
using LauncherKit.Infrastructure;

namespace LauncherKit.Core.Fomod;

/// <summary>
/// Presents FOMOD install steps as generic <see cref="ChoiceStep"/>s and maps the user's selection back to
/// <see cref="ConfiguredInstallStep"/>s. Each install step is shown through its first option group.
/// An option the script marks <see cref="OptionType.NotUsable"/> is shown as unavailable.
/// </summary>
public sealed class FomodChoices
{
	private readonly Dictionary<ChoiceStep, InstallStep> _installSteps = [];
	private readonly Dictionary<ChoiceOption, Option> _options = [];

	public IReadOnlyList<ChoiceStep> Steps { get; }

	public FomodChoices(IEnumerable<InstallStep> installSteps)
	{
		Steps = installSteps.Select(CreateStep).ToArray();
	}

	public IReadOnlyList<ConfiguredInstallStep> ToConfiguredInstallSteps(IReadOnlyList<ChoiceStepSelection> selections) =>
		selections
			.Select(selection => new ConfiguredInstallStep
			{
				Step = _installSteps.TryGetValue(selection.Step, out var installStep)
					? installStep
					: throw new ArgumentException($"Step '{selection.Step.Title}' is not a FOMOD step", nameof(selections)),
				SelectedOptions = selection.SelectedOptions
					.Select(choiceOption => _options.TryGetValue(choiceOption, out var option)
						? option
						: throw new ArgumentException($"Option '{choiceOption.Name}' is not a FOMOD option", nameof(selections)))
					.ToArray(),
			})
			.ToArray();

	private ChoiceStep CreateStep(InstallStep installStep)
	{
		var optionGroup = installStep.OptionGroups[0];
		var step = new ChoiceStep
		{
			Title = new ChoiceText.Content(installStep.Name),
			Subtitle = new ChoiceText.Content(optionGroup.Name),
			SelectionMode = ToSelectionMode(optionGroup.Type),
			Options = optionGroup.Options.Select(CreateOption).ToArray(),
		};
		_installSteps.Add(step, installStep);
		return step;
	}

	private ChoiceOption CreateOption(Option option)
	{
		var staticType = (option.OptionTypeResolver as StaticOptionTypeResolver)?.Type;
		var choiceOption = new ChoiceOption
		{
			Name = new ChoiceText.Content(option.Name),
			Description = new ChoiceText.Content(option.Description),
			IsRequired = staticType == OptionType.Required,
			UnavailableReason = staticType == OptionType.NotUsable ? ChoiceText.UnavailableFomodOption.Instance : (ChoiceText?)null,
		};
		_options.Add(choiceOption, option);
		return choiceOption;
	}

	private static ChoiceSelectionMode ToSelectionMode(OptionGroupType type) => type switch
	{
		OptionGroupType.SelectAny => ChoiceSelectionMode.Any,
		OptionGroupType.SelectAtLeastOne => ChoiceSelectionMode.AtLeastOne,
		OptionGroupType.SelectAtMostOne => ChoiceSelectionMode.AtMostOne,
		OptionGroupType.SelectExactlyOne => ChoiceSelectionMode.ExactlyOne,
		OptionGroupType.SelectAll => ChoiceSelectionMode.All,
		_ => throw new NotSupportedException($"Option group type {type} is not supported"),
	};
}
