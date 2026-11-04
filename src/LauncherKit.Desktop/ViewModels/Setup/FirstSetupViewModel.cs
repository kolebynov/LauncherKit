using CommunityToolkit.Mvvm.ComponentModel;
using LauncherKit.Core.Services.Abstractions;

namespace LauncherKit.Desktop.ViewModels.Setup;

public enum FirstSetupState { NotStarted, InProgress, Saving, Finished }

/// <summary>
/// Routes the first setup through its steps, built from every registered <see cref="ISetupStepProvider"/> by
/// <see cref="FirstSetupSteps.Create"/>. Each step validates and applies its own input to
/// <see cref="FirstSetupContext.Settings"/> and moves the setup with the next/back callbacks it is shown with. Moving
/// past the last step saves the settings and sets <see cref="FirstSetupState.Finished"/>. The application scope is
/// replaced only when the saved settings change the Skyrim directory; otherwise the consumer leaves the setup in the
/// current scope once it is finished. The setup is needed only when it has steps.
/// </summary>
public partial class FirstSetupViewModel : ViewModelBase
{
	private readonly FirstSetupContext _context;
	private readonly ISettingsService _settingsService;

	[ObservableProperty]
	public partial SetupStepViewModel? CurrentStep { get; private set; }
	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(IsFinished))]
	public partial FirstSetupState State { get; private set; }

	public IReadOnlyList<SetupStepViewModel> Steps { get; }

	/// <summary>Whether any step needs the user; consumers show the setup only then.</summary>
	public bool IsRequired => Steps.Count > 0;

	public bool IsFinished => State is FirstSetupState.Saving or FirstSetupState.Finished;

	public FirstSetupViewModel(IReadOnlyList<SetupStepViewModel> steps, FirstSetupContext context,
		ISettingsService settingsService)
	{
		Steps = steps;
		_context = context;
		_settingsService = settingsService;
	}

	/// <summary>Shows the first step; later calls do nothing. Without steps the setup finishes at once.</summary>
	public Task Start()
	{
		if (State != FirstSetupState.NotStarted) return Task.CompletedTask;
		State = FirstSetupState.InProgress;
		return GoTo(0);
	}

	private Task GoTo(int index)
	{
		// Hiding clears the shown step's callbacks, so only the current step can move the setup.
		CurrentStep?.Hide();
		if (index == Steps.Count) return Finish();

		var step = Steps[index];
		step.Show(() => GoTo(index + 1), index > 0 ? () => GoTo(index - 1) : null);
		CurrentStep = step;
		return Task.CompletedTask;
	}

	private async Task Finish()
	{
		State = FirstSetupState.Saving;
		await _settingsService.SaveSettings(_context.Settings);
		State = FirstSetupState.Finished;
	}
}
