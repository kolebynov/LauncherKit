using R3;
using LauncherKit.Desktop.Activation;

namespace LauncherKit.Desktop.Extensions;

public static class ActivationExtensions
{
	public static void WhenActivated(
		this IActivatableViewModel activatableViewModel, Action<CompositeDisposable> block)
	{
		ArgumentNullException.ThrowIfNull(activatableViewModel);
		ArgumentNullException.ThrowIfNull(block);

		activatableViewModel.Activator.AddActivationBlock((d, _) =>
		{
			block(d);
			return ValueTask.CompletedTask;
		});
	}

	public static void WhenActivated(
		this IActivatableViewModel activatableViewModel, Func<CompositeDisposable, CancellationToken, ValueTask> block)
	{
		ArgumentNullException.ThrowIfNull(activatableViewModel);
		ArgumentNullException.ThrowIfNull(block);

		activatableViewModel.Activator.AddActivationBlock(block);
	}
}
