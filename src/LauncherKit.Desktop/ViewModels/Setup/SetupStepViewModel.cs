using CommunityToolkit.Mvvm.Input;

namespace LauncherKit.Desktop.ViewModels.Setup;

/// <summary>
/// One first-setup step. The step owns its validation and its changes to <see cref="FirstSetupContext.Settings"/>,
/// then moves the setup with <see cref="GoNext"/> or <see cref="GoBack"/>. <see cref="FirstSetupViewModel"/> supplies
/// those callbacks while the step is shown and clears them when it hides the step, so a hidden step cannot move it.
/// </summary>
public abstract class SetupStepViewModel : ViewModelBase
{
	private Func<Task>? _goNext;
	private Func<Task>? _goBack;

	protected SetupStepViewModel() => BackCommand = new AsyncRelayCommand(GoBack, () => CanGoBack);

	public bool IsShown => _goNext != null;

	/// <summary>Whether the setup has a previous step to return to.</summary>
	public bool CanGoBack => _goBack != null;

	public AsyncRelayCommand BackCommand { get; }

	internal void Show(Func<Task> goNext, Func<Task>? goBack)
	{
		_goNext = goNext;
		_goBack = goBack;
		OnNavigationChanged();
		OnShown();
	}

	internal void Hide()
	{
		_goNext = null;
		_goBack = null;
		OnNavigationChanged();
		OnHidden();
	}

	/// <summary>Moves to the next step, or finishes the setup after the last one. Does nothing while hidden.</summary>
	protected Task GoNext() => _goNext?.Invoke() ?? Task.CompletedTask;

	/// <summary>Returns to the previous step. Does nothing while hidden or on the first step.</summary>
	protected Task GoBack() => _goBack?.Invoke() ?? Task.CompletedTask;

	/// <summary>Called when the step is shown, e.g. to read settings applied by earlier steps.</summary>
	protected virtual void OnShown()
	{
	}

	protected virtual void OnHidden()
	{
	}

	private void OnNavigationChanged()
	{
		OnPropertyChanged(nameof(IsShown));
		OnPropertyChanged(nameof(CanGoBack));
		BackCommand.NotifyCanExecuteChanged();
	}
}
