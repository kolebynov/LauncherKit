using System.Globalization;
using LauncherKit.Desktop.Services;
using Microsoft.Extensions.Time.Testing;
using LauncherKit.Infrastructure;
using LauncherKit.TestSupport;

namespace LauncherKit.Desktop.Tests.Services;

[TestFixture]
[NonParallelizable]
public sealed class LauncherKitScopeReloadServiceTests
{
	[Test]
	public void LanguageChange_RebuildsViewsWithoutReplacingExecutionScope()
	{
		var previous = CultureInfo.CurrentUICulture;
		var previousDefault = CultureInfo.DefaultThreadCurrentUICulture;
		try
		{
			var settings = new OptionsMonitorStub<AppSettings>(new AppSettings { Language = "ru" });
			var dispatcher = new QueuedUiDispatcher();
			var replacements = 0;
			var views = 0;
			using var sut = new LauncherKitScopeReloadService(settings, dispatcher,
				CreateLifetime(new AutoMocker(MockBehavior.Strict), () => replacements++, () => views++), new FakeTimeProvider());
			sut.Initialize();

			settings.Change(new AppSettings { Language = "en" });
			dispatcher.Drain();

			Assert.Multiple(() =>
			{
				Assert.That(replacements, Is.Zero);
				Assert.That(views, Is.EqualTo(1));
				Assert.That(CultureInfo.CurrentUICulture.Name, Is.EqualTo("en"));
			});
		}
		finally
		{
			CultureInfo.DefaultThreadCurrentUICulture = previousDefault;
			CultureInfo.CurrentUICulture = previous;
		}
	}

	[Test]
	public async Task SkyrimDirectoryChange_ReplacesScopeInOneDispatchTurnAfterDebounce()
	{
		var settings = new OptionsMonitorStub<AppSettings>(new AppSettings { SkyrimDirectory = "old" });
		var dispatcher = new SignalingDispatcher();
		var clock = new FakeTimeProvider();
		var events = new List<string>();
		using var sut = new LauncherKitScopeReloadService(settings, dispatcher,
			CreateLifetime(new AutoMocker(MockBehavior.Strict), () => events.Add("scope"), () => events.Add("views")), clock);
		sut.Initialize();

		settings.Change(new AppSettings { SkyrimDirectory = "new" });
		Assert.That(events, Is.Empty);
		clock.Advance(TimeSpan.FromMilliseconds(200));
		var dispatched = await dispatcher.Posted.Task.WaitAsync(TimeSpan.FromSeconds(5));
		dispatched();

		Assert.That(events, Is.EqualTo(new[] { "scope" }));
	}

	[Test]
	public void Dispose_UnsubscribesFromSettingsChanges()
	{
		var settings = new OptionsMonitorStub<AppSettings>(new AppSettings { Language = "ru" });
		var dispatcher = new QueuedUiDispatcher();
		var views = 0;
		var sut = new LauncherKitScopeReloadService(settings, dispatcher,
			CreateLifetime(new AutoMocker(MockBehavior.Strict), () => Assert.Fail("A disposed reload service cannot replace scopes."), () => views++),
			new FakeTimeProvider());
		sut.Initialize();
		sut.Dispose();

		settings.Change(new AppSettings { Language = "en" });
		dispatcher.Drain();

		Assert.That(views, Is.Zero);
	}

	private static IAvaloniaApplicationLifetime CreateLifetime(AutoMocker mocker, Action createNewScope, Action reloadViews)
	{
		var lifetime = mocker.GetMock<IAvaloniaApplicationLifetime>();
		lifetime.Setup(x => x.CreateNewScope()).Callback(createNewScope);
		lifetime.Setup(x => x.ReloadViews()).Callback(reloadViews);
		return lifetime.Object;
	}

	private sealed class SignalingDispatcher : IUiDispatcher
	{
		public TaskCompletionSource<Action> Posted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
		public void Post(Action action) => Posted.TrySetResult(action);
		public Task InvokeAsync(Action action) => throw new AssertionException("Reload is posted, not invoked.");
		public Task<TResult> InvokeAsync<TResult>(Func<TResult> action) =>
			throw new AssertionException("Reload is posted, not invoked.");
		public Task InvokeAsync(Func<Task> func) => throw new AssertionException("Reload is posted, not invoked.");
		public Task<TResult> InvokeAsync<TResult>(Func<Task<TResult>> action) =>
			throw new AssertionException("Reload is posted, not invoked.");
	}
}
