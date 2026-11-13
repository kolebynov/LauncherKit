using Microsoft.Extensions.Logging;
using R3;
using LauncherKit.Core.Choices;
using LauncherKit.Core.Feed.Install;
using LauncherKit.Core.Models;
using LauncherKit.Infrastructure;

namespace LauncherKit.Core.AppTasks.MicroTasks;

/// <summary>
/// Runs a feed <see cref="InstallAction.SelectChoices"/> action: shows its pages and records, under each option's ID in
/// its install step's context, whether the option was selected.
/// </summary>
internal sealed class SelectChoicesMicroTask : IAppMicroTask
{
	private static readonly ChoiceText EmptyText = new ChoiceText.Content(string.Empty);

	private readonly InstallStepContext _context;
	private readonly ILogger<SelectChoicesMicroTask> _logger;
	private readonly InstallAction.SelectChoices _action;

	public MicroTaskDescription Description => MicroTaskDescription.SelectChoices.Instance;

	public SelectChoicesMicroTask(InstallStepContext context, ILogger<SelectChoicesMicroTask> logger,
		InstallAction.SelectChoices action)
	{
		_context = context;
		_logger = logger;
		_action = action;
	}

	public async ValueTask Execute(ISubject<MicroTaskStep> stepSubject, CancellationToken cancellationToken)
	{
		// Feed options by the choice option shown for them; choice options are compared by reference.
		var feedOptions = new Dictionary<ChoiceOption, InstallChoiceOption>(ReferenceEqualityComparer.Instance);
		var steps = _action.Steps
			.Select(step => new ChoiceStep
			{
				Title = new ChoiceText.Content(step.Title.Resolve()),
				Subtitle = step.Subtitle is { } subtitle ? new ChoiceText.Content(subtitle.Resolve()) : EmptyText,
				SelectionMode = step.SelectionMode,
				Options = step.Options
					.Select(option =>
					{
						var choiceOption = new ChoiceOption
						{
							Name = new ChoiceText.Content(option.Name.Resolve()),
							Description = option.Description is { } description
								? new ChoiceText.Content(description.Resolve())
								: EmptyText,
							IsRequired = option.IsRequired,
							IsSelectedByDefault = option.IsSelectedByDefault,
						};
						feedOptions.Add(choiceOption, option);
						return choiceOption;
					})
					.ToArray(),
			})
			.ToArray();

		using var choiceSelectionContext = new ChoiceSelectionContext(new ChoiceText.Content(_action.Title.Resolve()), steps,
			cancellationToken);
		stepSubject.OnNext(new MicroTaskStep.SelectChoices(choiceSelectionContext));
		var selections = await choiceSelectionContext.ResultTask;

		var selected = selections.SelectMany(x => x.SelectedOptions).ToHashSet(ReferenceEqualityComparer.Instance);
		foreach (var (choiceOption, option) in feedOptions)
		{
			_context.SetValue(option.Id,
				selected.Contains(choiceOption) ? InstallAction.SelectChoices.Selected : InstallAction.SelectChoices.NotSelected);
		}

		_logger.LogInformation("Selected choice options: {OptionIds}",
			string.Join(", ", feedOptions.Where(x => selected.Contains(x.Key)).Select(x => x.Value.Id)));
	}
}
