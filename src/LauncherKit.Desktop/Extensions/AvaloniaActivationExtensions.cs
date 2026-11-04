using Avalonia;
using LauncherKit.Desktop.Activation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using R3;
using LauncherKit.Core.Extensions;

namespace LauncherKit.Desktop.Extensions;

public static class AvaloniaActivationExtensions
{
	public static void UseActivation(this Window window)
	{
		window.WhenActivated<object>((_, _, _) => ValueTask.CompletedTask);
	}

	public static void UseActivation(this UserControl control)
	{
		control.WhenActivated<object>((_, _, _) => ValueTask.CompletedTask);
	}

	public static void WhenActivated<TViewModel>(
		this Window window, Action<TViewModel, CompositeDisposable> block)
		where TViewModel : class
	{
		window.WhenActivated<TViewModel>((vm, d, _) =>
		{
			block(vm, d);
			return ValueTask.CompletedTask;
		});
	}

	public static void WhenActivated<TViewModel>(
		this UserControl control, Action<TViewModel, CompositeDisposable> block)
		where TViewModel : class
	{
		control.WhenActivated<TViewModel>((vm, d, _) =>
		{
			block(vm, d);
			return ValueTask.CompletedTask;
		});
	}

	public static void WhenActivated<TViewModel>(
		this Window window, Func<TViewModel, CompositeDisposable, CancellationToken, ValueTask> block)
		where TViewModel : class
	{
		_ = window ?? throw new ArgumentNullException(nameof(window));
		_ = block ?? throw new ArgumentNullException(nameof(block));

		var viewActivationEvents = Observable.FromEventHandler<RoutedEventArgs>(
				h => window.Loaded += h, h => window.Loaded -= h)
			.Select(_ => true)
			.Merge(Observable
				.FromEventHandler<RoutedEventArgs>(
					h => window.Unloaded += h, h => window.Unloaded -= h)
				.Select(_ => false));

		WhenActivatedInternal(window, block, viewActivationEvents);
	}

	public static void WhenActivated<TViewModel>(
		this UserControl control, Func<TViewModel, CompositeDisposable, CancellationToken, ValueTask> block)
		where TViewModel : class
	{
		_ = control ?? throw new ArgumentNullException(nameof(control));
		_ = block ?? throw new ArgumentNullException(nameof(block));

		var viewActivationEvents = Observable.FromEventHandler<VisualTreeAttachmentEventArgs>(
				h => control.AttachedToVisualTree += h, h => control.AttachedToVisualTree -= h)
			.Select(_ => true)
			.Merge(Observable
				.FromEventHandler<VisualTreeAttachmentEventArgs>(
					h => control.DetachedFromVisualTree += h, h => control.DetachedFromVisualTree -= h)
				.Select(_ => false));

		WhenActivatedInternal(control, block, viewActivationEvents);
	}

	private static void WhenActivatedInternal<TViewModel>(
		this Control control, Func<TViewModel, CompositeDisposable, CancellationToken, ValueTask> block,
		Observable<bool> viewActivationEvents)
		where TViewModel : class
	{
		var activationEvents = viewActivationEvents
			.CombineLatest(
				control.ObservePropertyChanged(x => x.DataContext),
				(activated, dataContext) => new ActivationEvent<TViewModel>(activated && dataContext != null, dataContext as TViewModel))
			.DistinctUntilChanged();

		HandleViewActivation(block, activationEvents);
		HandleViewModelActivation(activationEvents);
	}

	private static void HandleViewActivation<TViewModel>(
		Func<TViewModel, CompositeDisposable, CancellationToken, ValueTask> block,
		Observable<ActivationEvent<TViewModel>> activationEvents)
		where TViewModel : class
	{
		activationEvents
			.SubscribeAwait(
#pragma warning disable CA2000
				(ActivationDisposable: new SerialDisposable(), Block: block),
#pragma warning restore CA2000
				static (evt, state, ct) =>
				{
					var task = Dispatcher.UIThread.InvokeAsync(async () =>
					{
						var (activated, viewModel) = evt;
						var (activationDisposable, block) = state;
						activationDisposable.Disposable = Disposable.Empty;
						if (!activated)
						{
							return;
						}

						var compositeDisposable = new CompositeDisposable();
						try
						{
							await block(viewModel!, compositeDisposable, ct);
							activationDisposable.Disposable = compositeDisposable;
						}
						catch (Exception e)
						{
							compositeDisposable.Dispose();
							if (e is not OperationCanceledException)
							{
								throw;
							}
						}
					});

					return task.AsValueTask();
				},
				(_, state) => state.ActivationDisposable.Dispose());
	}

	private static void HandleViewModelActivation<TViewModel>(Observable<ActivationEvent<TViewModel>> activationEvents)
		where TViewModel : class
	{
		activationEvents
			.SubscribeAwait(
#pragma warning disable CA2000
				new SerialDisposable(),
#pragma warning restore CA2000
				static (evt, vmActivationDisposable, ct) =>
				{
					var task = Dispatcher.UIThread.InvokeAsync(async () =>
					{
						var (activated, viewModel) = evt;
						vmActivationDisposable.Disposable = Disposable.Empty;
						if (!activated || viewModel is not IActivatableViewModel activatableViewModel)
						{
							return;
						}

						try
						{
							vmActivationDisposable.Disposable = await activatableViewModel.Activator.Activate(ct);
						}
						catch (OperationCanceledException)
						{
						}
					});

					return task.AsValueTask();
				},
				(_, vmActivationDisposable) => vmActivationDisposable.Dispose());
	}

	private readonly record struct ActivationEvent<TViewModel>(bool Activated, TViewModel? ViewModel)
		where TViewModel : class;
}