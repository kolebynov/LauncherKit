namespace LauncherKit.Core.Models;

internal sealed class InstallContext
{
	private readonly Dictionary<int, InstallStepContext> _stepContexts = new();

	public InstallStepContext GetContextForStep(int stepIndex)
	{
		if (_stepContexts.TryGetValue(stepIndex, out var stepContext))
		{
			return stepContext;
		}

		return _stepContexts[stepIndex] = new InstallStepContext();
	}
}
