using CommunityToolkit.Mvvm.ComponentModel;
using LauncherKit.Desktop.Extensions;
using LauncherKit.Desktop.Services;
using LauncherKit.Infrastructure;
using R3;
using LauncherKit.Core.Feed;

namespace LauncherKit.Desktop.ViewModels;

public closed record FeedLoadStatus
{
	private FeedLoadStatus() { }
	public sealed record Idle : FeedLoadStatus;
	public sealed record Loading : FeedLoadStatus;
	public sealed record Failed(Exception Exception) : FeedLoadStatus;
}

/// <summary>Projects feed availability without requesting notifications or choosing their presentation.</summary>
public partial class FeedLoadStatusViewModel : ViewModelBase, IDisposable
{
	public static readonly TimeSpan ProgressDelay = TimeSpan.FromMilliseconds(500);
	private readonly SerialDisposable _delay = new();
	private readonly IDisposable _subscription;
	private readonly TimeProvider _timeProvider;
	private readonly IUiDispatcher _dispatcher;

	[ObservableProperty]
	public partial FeedLoadStatus Status { get; private set; } = FeedLoadStatus.Idle.Instance;

	public FeedLoadStatusViewModel(IFeedProvider feedProvider, TimeProvider timeProvider, IUiDispatcher dispatcher)
	{
		_timeProvider = timeProvider;
		_dispatcher = dispatcher;
		_subscription = feedProvider.LoadEvents.ObserveOnUi(dispatcher).Subscribe(OnLoadEvent);
	}

	private void OnLoadEvent(FeedLoadEvent loadEvent)
	{
		_delay.Disposable = null;
		switch (loadEvent)
		{
			case FeedLoadEvent.Started:
				_delay.Disposable = Observable.Timer(ProgressDelay, _timeProvider).ObserveOnUi(_dispatcher)
					.Subscribe(_ => Status = FeedLoadStatus.Loading.Instance);
				break;
			case FeedLoadEvent.Succeeded:
				Status = FeedLoadStatus.Idle.Instance;
				break;
			case FeedLoadEvent.Failed(var exception):
				Status = new FeedLoadStatus.Failed(exception);
				break;
		}
	}

	public void Dispose()
	{
		Dispose(true);
		GC.SuppressFinalize(this);
	}

	protected virtual void Dispose(bool disposing)
	{
		if (!disposing)
			return;
		_subscription.Dispose();
		_delay.Dispose();
	}
}
