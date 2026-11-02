﻿using LauncherKit.Core.Choices;
using LauncherKit.Core.Models;

namespace LauncherKit.Core.AppTasks.MicroTasks;

public union MicroTaskStep(MicroTaskStep.Progress, MicroTaskStep.MicroOperation, MicroTaskStep.SelectChoices)
{
	public sealed record Progress(IOperationProgress OperationProgress);

	public sealed record MicroOperation(MicroTaskDescription? Description);

	public sealed record SelectChoices(ChoiceSelectionContext Context);
}