using CommunityToolkit.Mvvm.ComponentModel;
using LauncherKit.Desktop.Extensions;
using CommunityToolkit.Mvvm.Input;
using LauncherKit.Core.Services.Abstractions;

namespace LauncherKit.Desktop.ViewModels;

/// <summary>
/// Checks that a download server works. The check falls back to the other server, which then becomes the preferred
/// one. The setup can continue once the check finishes, even when no server works: the user is then warned that
/// nothing can be downloaded or updated.
/// </summary>
public partial class CheckAvailabilityViewModel : BaseActivatableViewModel
{
	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(IsAvailable), nameof(IsUnavailable))]
	public partial CheckAvailabilityStatus CheckStatus { get; private set; }

	public bool IsAvailable => CheckStatus == CheckAvailabilityStatus.Success;

	public bool IsUnavailable => CheckStatus == CheckAvailabilityStatus.Failed;

	public AsyncRelayCommand CheckAvailabilityCommand { get; }

	public AsyncRelayCommand FinishCommand { get; }

	public CheckAvailabilityViewModel(IFeedAvailabilityChecker availabilityChecker,
		Func<CancellationToken, Task> onFinishAction)
	{
		CheckAvailabilityCommand = new AsyncRelayCommand(async () =>
		{
			CheckStatus = CheckAvailabilityStatus.InProgress;
			var isAvailable = await availabilityChecker.CheckFeed();
			CheckStatus = isAvailable ? CheckAvailabilityStatus.Success : CheckAvailabilityStatus.Failed;
		});

		FinishCommand = new AsyncRelayCommand(
			onFinishAction,
			() => CheckStatus is CheckAvailabilityStatus.Success or CheckAvailabilityStatus.Failed);

		this.WhenActivated(_ => CheckAvailabilityCommand.Execute(null));
	}

	partial void OnCheckStatusChanged(CheckAvailabilityStatus value) => FinishCommand.NotifyCanExecuteChanged();
}
