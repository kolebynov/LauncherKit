using CommunityToolkit.Mvvm.ComponentModel;
using LauncherKit.Desktop.ViewModels;
using CommunityToolkit.Mvvm.Input;
using LauncherKit.Core.Feed;

namespace LauncherKit.Desktop.ViewModels;

public sealed partial class ActivateBetaCodeViewModel : ViewModelBase
{
	private readonly IFeedProvider _feedProvider;

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(IsActivated))]
	public partial ActivateBetaCodeResult? Result { get; private set; }

	[ObservableProperty]
	public partial string? BetaCode { get; set; }

	public bool IsActivated => Result is ActivateBetaCodeResult.Success;

	public ActivateBetaCodeViewModel(IFeedProvider feedProvider) => _feedProvider = feedProvider;

	[RelayCommand]
	private async Task ActivateBetaCode() => Result = string.IsNullOrEmpty(BetaCode)
		? new ActivateBetaCodeResult.InvalidBetaCode()
		: await _feedProvider.ActivateBetaCode(BetaCode, CancellationToken.None);
}
