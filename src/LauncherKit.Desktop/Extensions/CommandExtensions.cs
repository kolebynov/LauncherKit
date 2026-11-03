using CommunityToolkit.Mvvm.Input;
using R3;

namespace LauncherKit.Desktop.Extensions;

public static class CommandExtensions
{
	public static Observable<bool> ObserveCanExecuteChange(this RelayCommand command) =>
		Observable.FromEventHandler(h => command.CanExecuteChanged += h, h => command.CanExecuteChanged -= h)
			.Select(command, static (_, command) => command.CanExecute(null))
			.Prepend(command.CanExecute(null));

	public static Observable<bool> ObserveCanExecuteChange(this AsyncRelayCommand command) =>
		Observable.FromEventHandler(h => command.CanExecuteChanged += h, h => command.CanExecuteChanged -= h)
			.Select(command, static (_, command) => command.CanExecute(null))
			.Prepend(command.CanExecute(null));
}