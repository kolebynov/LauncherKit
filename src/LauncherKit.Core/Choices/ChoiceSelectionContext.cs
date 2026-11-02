﻿using LauncherKit.Core.Models;

namespace LauncherKit.Core.Choices;

/// <summary>
/// Asks the user to go through <see cref="Steps"/> and completes with one <see cref="ChoiceStepSelection"/> per step,
/// in the same order as <see cref="Steps"/>.
/// </summary>
public sealed class ChoiceSelectionContext : BaseAsyncResult<IReadOnlyList<ChoiceStepSelection>>
{
	public ChoiceText Title { get; }

	public IReadOnlyList<ChoiceStep> Steps { get; }

	public ChoiceSelectionContext(ChoiceText title, IReadOnlyList<ChoiceStep> steps, CancellationToken cancellationToken)
		: base(cancellationToken)
	{
		Title = title;
		Steps = steps;
	}
}
