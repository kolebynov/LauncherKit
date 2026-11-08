﻿using LauncherKit.Infrastructure;
using Microsoft.Extensions.Logging;
using R3;
using LauncherKit.Core.Choices;
using LauncherKit.Core.Models;
using LauncherKit.Core.Services.Abstractions;

namespace LauncherKit.Core.AppTasks.MicroTasks;

internal sealed class SelectRequiredModMicroTask : IAppMicroTask
{
	private const string SelectedModKey = "SelectedMod";

	private readonly InstallStepContext _context;
	private readonly ILogger<SelectRequiredModMicroTask> _logger;
	private readonly IModOrganizerService _modOrganizerService;
	private readonly IReadOnlyList<string> _requiredMods;

	public MicroTaskDescription Description { get; } = MicroTaskDescription.SelectRequiredMod.Instance;

	public SelectRequiredModMicroTask(InstallStepContext context, ILogger<SelectRequiredModMicroTask> logger,
		IModOrganizerService modOrganizerService, IReadOnlyList<string> requiredMods)
	{
		_context = context;
		_logger = logger;
		_modOrganizerService = modOrganizerService;
		_requiredMods = requiredMods;
	}

	public async ValueTask Execute(ISubject<MicroTaskStep> stepSubject, CancellationToken cancellationToken)
	{
		var alreadyActivatedMod = (await _modOrganizerService.GetModList(cancellationToken))
			.FirstOrDefault(x => x.IsActivated && _requiredMods.Contains(x.Name));
		if (alreadyActivatedMod != default)
		{
			_logger.LogInformation("Mod {ModName} is already activated", alreadyActivatedMod.Name);
			_context.SetValue(SelectedModKey, alreadyActivatedMod.Name);
			return;
		}

		var mods = _modOrganizerService.InstalledMods
			.Where(mod => _requiredMods.Contains(mod.Name))
			.ToArray();
		if (mods.Length == 0)
		{
			throw new InvalidOperationException($"None of the required mods is installed: {string.Join(", ", _requiredMods)}");
		}

		var options = mods
			.Select((mod, index) => new ChoiceOption
			{
				Name = new ChoiceText.Content(mod.Name),
				Description = new ChoiceText.Content(mod.Description ?? string.Empty),
				IsSelectedByDefault = index == 0,
			})
			.ToArray();
		ChoiceStep step = new()
		{
			Title = ChoiceText.RequiredModsStepTitle.Instance,
			Subtitle = ChoiceText.SelectOneModSubtitle.Instance,
			SelectionMode = ChoiceSelectionMode.ExactlyOne,
			Options = options,
		};

		using var choiceSelectionContext = new ChoiceSelectionContext(new ChoiceText.SelectRequiredModTitle(_requiredMods), [step],
			cancellationToken);
		stepSubject.OnNext(new MicroTaskStep.SelectChoices(choiceSelectionContext));
		var selections = await choiceSelectionContext.ResultTask;
		var selectedMod = mods[Array.IndexOf(options, selections.Single().SelectedOptions.Single())];
		_logger.LogInformation("Mod {ModName} is selected", selectedMod.Name);
		_context.SetValue(SelectedModKey, selectedMod.Name);
	}
}