using CommunityToolkit.Mvvm.ComponentModel;
using R3;
using LauncherKit.Core.Choices;

namespace LauncherKit.Desktop.ViewModels;

public sealed partial class ChoiceStepViewModel : ViewModelBase
{

	public ChoiceStep Step { get; }

	public ChoiceSelectionMode SelectionMode => Step.SelectionMode;

	public ChoiceText Title => Step.Title;

	public ChoiceText Subtitle => Step.Subtitle;

	public IReadOnlyList<ChoiceOptionViewModel> Options { get; }

	[ObservableProperty]
	public partial bool AllRequiredOptionsSelected { get; private set; }

	public ChoiceStepViewModel(ChoiceStep step)
	{
		Step = step;
		Options = step.Options.Select(x => new ChoiceOptionViewModel(x, CanDeselect)).ToList();

		foreach (var optionViewModel in Options)
		{
			optionViewModel.ObservePropertyChanged(x => x.IsSelected)
				.Subscribe(isSelected =>
				{
					AllRequiredOptionsSelected = CalculateAllRequiredOptionsSelected();
					if (isSelected && SelectionMode == ChoiceSelectionMode.ExactlyOne)
					{
						DeselectOther(optionViewModel);
					}

					foreach (var option in Options)
					{
						option.UpdateCanToggle();
					}
				});
		}
		AllRequiredOptionsSelected = CalculateAllRequiredOptionsSelected();
	}

	public ChoiceStepSelection GetSelection() => new()
	{
		Step = Step,
		SelectedOptions = Options.Where(x => x.IsSelected).Select(x => x.Option).ToList(),
	};

	private bool CalculateAllRequiredOptionsSelected()
	{
		var requiredOptionsSelected = Options.Where(x => x.IsRequired).All(x => x.IsSelected);
		var optionalSelected = SelectionMode switch
		{
			ChoiceSelectionMode.AtLeastOne or ChoiceSelectionMode.ExactlyOne => Options.Any(x => x.IsSelected),
			ChoiceSelectionMode.AtMostOne => Options.Count(x => x.IsSelected) <= 1,
			ChoiceSelectionMode.All => Options.Where(x => x.IsAvailable).All(x => x.IsSelected),
			ChoiceSelectionMode.Any => true,
			var mode => throw new ArgumentOutOfRangeException(
				"selectionMode", mode, "Invalid choice selection mode"),
		};

		return requiredOptionsSelected && optionalSelected;
	}

	/// <summary>
	/// Whether <paramref name="option"/> may be deselected: modes that need a selection never let the user
	/// deselect the last selected option, so a step cannot drop back to an empty selection.
	/// </summary>
	private bool CanDeselect(ChoiceOptionViewModel option) => SelectionMode switch
	{
		ChoiceSelectionMode.AtLeastOne or ChoiceSelectionMode.ExactlyOne or ChoiceSelectionMode.All =>
			Options.Any(x => x != option && x.IsSelected),
		_ => true,
	};

	private void DeselectOther(ChoiceOptionViewModel selectedOption)
	{
		foreach (var optionViewModel in Options.Where(x => x is { IsRequired: false, IsSelected: true } && x != selectedOption))
		{
			optionViewModel.IsSelected = false;
		}
	}

	public sealed partial class ChoiceOptionViewModel : ViewModelBase
	{
		private readonly Func<ChoiceOptionViewModel, bool> _canDeselect;
		private bool _isSelected;

		public ChoiceOption Option { get; }

		public ChoiceText Name => Option.Name;

		public ChoiceText Description => Option.Description;

		public bool IsSelected
		{
			get => _isSelected;
			set => SetProperty(ref _isSelected, IsRequired || (value ? IsAvailable : _isSelected && !_canDeselect(this)));
		}

		/// <summary>
		/// Whether the user can change <see cref="IsSelected"/>. The view's check box ignores clicks otherwise: a bound
		/// check box does not pick up a value the setter rejects, so it would show a state the option does not have.
		/// </summary>
		[ObservableProperty]
		public partial bool CanToggle { get; private set; }

		public bool IsRequired => Option.IsRequired;

		public bool IsAvailable => Option.IsAvailable;

		public ChoiceText? UnavailableReason => Option.UnavailableReason;

		public ChoiceOptionViewModel(ChoiceOption option, Func<ChoiceOptionViewModel, bool> canDeselect)
		{
			Option = option;
			_canDeselect = canDeselect;
			_isSelected = IsRequired || (IsAvailable && option.IsSelectedByDefault);
		}

		internal void UpdateCanToggle() => CanToggle = IsAvailable && !IsRequired && (!IsSelected || _canDeselect(this));
	}
}
