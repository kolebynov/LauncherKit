using LauncherKit.Infrastructure;
using Microsoft.Extensions.Time.Testing;
using LauncherKit.Desktop.Services;
using LauncherKit.Desktop.ViewModels;
using R3;
using LauncherKit.Core.Feed;

namespace LauncherKit.Desktop.Tests.Initialization;

[TestFixture]
public sealed class FeedLoadStatusViewModelTests
{
	[Test]
	public void FastLoad_DoesNotExposeLoadingState()
	{
		using var events = new Subject<FeedLoadEvent>();
		var clock = new FakeTimeProvider();
		using var sut = Create(events, clock);
		events.OnNext(FeedLoadEvent.Started.Instance);
		clock.Advance(FeedLoadStatusViewModel.ProgressDelay - TimeSpan.FromMilliseconds(1));
		Assert.That(sut.Status, Is.TypeOf<FeedLoadStatus.Idle>());
		events.OnNext(FeedLoadEvent.Succeeded.Instance);
		clock.Advance(FeedLoadStatusViewModel.ProgressDelay);
		Assert.That(sut.Status, Is.TypeOf<FeedLoadStatus.Idle>());
	}

	[Test]
	public void SlowLoad_ExposesLoadingThenIdle()
	{
		using var events = new Subject<FeedLoadEvent>();
		var clock = new FakeTimeProvider();
		using var sut = Create(events, clock);
		events.OnNext(FeedLoadEvent.Started.Instance);
		clock.Advance(FeedLoadStatusViewModel.ProgressDelay);
		Assert.That(sut.Status, Is.TypeOf<FeedLoadStatus.Loading>());
		events.OnNext(FeedLoadEvent.Succeeded.Instance);
		Assert.That(sut.Status, Is.TypeOf<FeedLoadStatus.Idle>());
	}

	[Test]
	public void Failure_PreservesExceptionAndCancelsDelayedLoading()
	{
		using var events = new Subject<FeedLoadEvent>();
		var clock = new FakeTimeProvider();
		using var sut = Create(events, clock);
		var error = new InvalidOperationException("feed unavailable");
		events.OnNext(FeedLoadEvent.Started.Instance);
		events.OnNext(new FeedLoadEvent.Failed(error));
		clock.Advance(FeedLoadStatusViewModel.ProgressDelay);
		Assert.That(sut.Status, Is.EqualTo(new FeedLoadStatus.Failed(error)));
		events.OnNext(FeedLoadEvent.Started.Instance);
		events.OnNext(FeedLoadEvent.Succeeded.Instance);
		Assert.That(sut.Status, Is.TypeOf<FeedLoadStatus.Idle>());
	}

	private static FeedLoadStatusViewModel Create(Observable<FeedLoadEvent> events, TimeProvider clock)
	{
		var feed = new Mock<IFeedProvider>(MockBehavior.Strict);
		feed.SetupGet(x => x.LoadEvents).Returns(events);
		return new FeedLoadStatusViewModel(feed.Object, clock, ImmediateDispatcher.Instance);
	}

	private sealed class ImmediateDispatcher : IUiDispatcher
	{
		public void Post(Action action) => action();
	}
}
