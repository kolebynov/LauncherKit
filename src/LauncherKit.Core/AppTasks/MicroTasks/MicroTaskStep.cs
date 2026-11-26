﻿using LauncherKit.Core.Choices;
using LauncherKit.Core.ModDependencies;
using LauncherKit.Core.Models;

namespace LauncherKit.Core.AppTasks.MicroTasks;

public union MicroTaskStep(
	MicroTaskStep.Progress,
	MicroTaskStep.MicroOperation,
	MicroTaskStep.SelectChoices,
	MicroTaskStep.ConfirmModUpgrades)
{
	public sealed record Progress(IOperationProgress OperationProgress);

	public sealed record MicroOperation(MicroTaskDescription? Description);

	public sealed record SelectChoices(ChoiceSelectionContext Context);

	/// <summary>Asks before a mod installation updates installed dependencies.</summary>
	public sealed record ConfirmModUpgrades(ModUpgradesConfirmation Confirmation);
}