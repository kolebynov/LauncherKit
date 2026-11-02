using CommunityToolkit.Mvvm.ComponentModel;
using LauncherKit.Desktop.ViewModels;
using CommunityToolkit.Mvvm.Input;
using R3;
using LauncherKit.Core.Choices;

namespace LauncherKit.Desktop.ViewModels;

public sealed partial class ChoiceSelectionViewModel : ViewModelBase
{
	private readonly ChoiceSelectionContext _context;
	private int _currentStepIndex;

	public ChoiceText Title => _context.Title;
	public IReadOnlyList<ChoiceStepViewModel> AllSteps { get; }

	[ObservableProperty]
	public partial ChoiceStepViewModel? CurrentStep { get; private set; }

	[ObservableProperty]
	public partial bool IsFinished { get; private set; }

	public bool CanGoBack => _currentStepIndex > 0 && !IsFinished;
	public bool CanGoNext => _currentStepIndex < AllSteps.Count - 1 && CurrentStep?.AllRequiredOptionsSelected == true && !IsFinished;
	public bool IsLastStep => _currentStepIndex >= AllSteps.Count - 1;
	public bool FullyConfigured => IsLastStep && AllSteps.All(x => x.AllRequiredOptionsSelected);
	private bool CanAdvanceOrFinish => !IsFinished && (CanGoNext || FullyConfigured);

	public ChoiceSelectionViewModel(ChoiceSelectionContext context)
	{
		_context = context;
		AllSteps = context.Steps.Select(x => new ChoiceStepViewModel(x)).ToArray();
		CurrentStep = AllSteps.Count == 0 ? null : AllSteps[0];
		foreach (var step in AllSteps)
		{
			step.ObservePropertyChanged(x => x.AllRequiredOptionsSelected).Subscribe(_ => UpdateNavigation());
		}
	}

	[RelayCommand(CanExecute = nameof(CanAdvanceOrFinish))]
	public void GoNext()
	{
		if (!CanAdvanceOrFinish)
		{
			return;
		}

		if (CanGoNext)
		{
			CurrentStep = AllSteps[++_currentStepIndex];
		}
		else
		{
			IsFinished = true;
			_context.SetResult(GetSelections());
		}

		UpdateNavigation();
	}

	[RelayCommand(CanExecute = nameof(CanGoBack))]
	public void GoBack()
	{
		if (!CanGoBack)
		{
			return;
		}

		CurrentStep = AllSteps[--_currentStepIndex];
		UpdateNavigation();
	}

	/// <summary>Returns the selection of every step, in step order.</summary>
	public IReadOnlyList<ChoiceStepSelection> GetSelections() => AllSteps.Select(x => x.GetSelection()).ToList();

	private void UpdateNavigation()
	{
		OnPropertyChanged(nameof(CanGoBack));
		OnPropertyChanged(nameof(CanGoNext));
		OnPropertyChanged(nameof(IsLastStep));
		OnPropertyChanged(nameof(FullyConfigured));
		GoBackCommand.NotifyCanExecuteChanged();
		GoNextCommand.NotifyCanExecuteChanged();
	}
}
