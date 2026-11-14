using LauncherKit.Core.Choices;
using LauncherKit.Core.Feed;
using LauncherKit.Infrastructure;

namespace LauncherKit.Core.AppTasks.ModpackComponents;

public sealed class ModpackComponentChoices
{
	public static readonly ChoiceText Title = ChoiceText.ModpackComponentsTitle.Instance;

	private readonly ModpackComponentsSelection _selection;
	private readonly List<(string Id, ChoiceStep Step, IReadOnlyList<ModComponentOption> Offers)> _steps = [];

	public IReadOnlyList<ChoiceStep> Steps { get; }

	public ModpackComponentChoices(ModpackComponentsOffer offer, Func<DownloadInfo, long> getDownloadSize)
	{
		_selection = offer.Selection;
		foreach (var component in offer.Components)
		{
			var defaultOption = component.Options.FirstOrDefault(x => x is { IsDefault: true, InstallTask: not null });
			var options = component.Options.Select(option => new ChoiceOption
			{
				Name = component.Definition.Kind == InstallComponentKind.Mod
					? ChoiceText.YesOption.Instance
					: new ChoiceText.Content(option.Name),
				Description = new ChoiceText.ComponentOptionDescription(option.Name, option.OptionDescription,
					option.InstallTask == null ? null : option.Downloads.Sum(getDownloadSize)),
				IsSelectedByDefault = ReferenceEquals(option, defaultOption),
				UnavailableReason = option.InstallTask == null ? ChoiceText.UnavailableModNotUpdated.Instance : (ChoiceText?)null,
			}).ToList();
			options.Add(new ChoiceOption { Name = ChoiceText.NoneOption.Instance, Description = new ChoiceText.Content(""), IsSelectedByDefault = defaultOption == null });
			var step = new ChoiceStep
			{
				Title = new ChoiceText.Content(component.Definition.Title.Resolve()),
				Subtitle = new ChoiceText.Content(component.Definition.Description.Resolve()),
				SelectionMode = ChoiceSelectionMode.ExactlyOne,
				Options = options,
			};
			_steps.Add((component.Definition.Id, step, component.Options));
		}

		Steps = _steps.Select(x => x.Step).ToArray();
	}

	public void Apply(IReadOnlyList<ChoiceStepSelection> selections)
	{
		foreach (var (id, step, offers) in _steps)
		{
			var selected = selections.Single(x => x.Step == step).SelectedOptions.Single();
			var index = 0;
			while (!ReferenceEquals(step.Options[index], selected))
			{
				index++;
			}
			_selection.InstallTasks[id] = index < offers.Count ? offers[index].InstallTask : null;
		}

		_selection.IsApplied = true;
	}
}
