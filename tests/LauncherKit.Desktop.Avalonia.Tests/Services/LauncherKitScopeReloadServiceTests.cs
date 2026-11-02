using System.Globalization;
using LauncherKit.Desktop.Avalonia;
using LauncherKit.Desktop.Avalonia.Extensions;
using LauncherKit.Desktop.Avalonia.Services;
using LauncherKit.Desktop.Extensions;
using LauncherKit.Desktop.Services;
using Microsoft.Extensions.Time.Testing;
using LauncherKit.Infrastructure;
using LauncherKit.TestSupport;

namespace LauncherKit.Desktop.Avalonia.Tests.Services;

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
			var dispatcher = new QueuedDispatcher();
			var replacements = 0;
			var shells = 0;
			var views = 0;
			using var sut = new LauncherKitScopeReloadService(settings, dispatcher,
				CreateOptions(() => shells++, () => views++), new FakeTimeProvider());
			sut.Start(() => replacements++);

			settings.Change(new AppSettings { Language = "en" });
			dispatcher.Drain();

			Assert.Multiple(() =>
			{
				Assert.That(replacements, Is.Zero);
				Assert.That(shells, Is.Zero);
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
	public async Task SkyrimDirectoryChange_ReplacesScopeThenRefreshesShellAfterDebounce()
	{
		var settings = new OptionsMonitorStub<AppSettings>(new AppSettings { SkyrimDirectory = "old" });
		var dispatcher = new SignalingDispatcher();
		var clock = new FakeTimeProvider();
		var events = new List<string>();
		using var sut = new LauncherKitScopeReloadService(settings, dispatcher,
			CreateOptions(() => events.Add("shell"), () => events.Add("views")), clock);
		sut.Start(() => events.Add("scope"));

		settings.Change(new AppSettings { SkyrimDirectory = "new" });
		Assert.That(events, Is.Empty);
		clock.Advance(TimeSpan.FromMilliseconds(200));
		var dispatched = await dispatcher.Posted.Task.WaitAsync(TimeSpan.FromSeconds(5));
		dispatched();

		Assert.That(events, Is.EqualTo(new[] { "scope", "shell" }));
	}

	[Test]
	public void Dispose_UnsubscribesFromSettingsChanges()
	{
		var settings = new OptionsMonitorStub<AppSettings>(new AppSettings { Language = "ru" });
		var dispatcher = new QueuedDispatcher();
		var views = 0;
		var sut = new LauncherKitScopeReloadService(settings, dispatcher,
			CreateOptions(() => { }, () => views++), new FakeTimeProvider());
		sut.Start(() => Assert.Fail("A disposed reload service cannot replace scopes."));
		sut.Dispose();

		settings.Change(new AppSettings { Language = "en" });
		dispatcher.Drain();

		Assert.That(views, Is.Zero);
	}

	[Test]
	public void ConfigurationReplacement_AndShellPublicationShareOneDispatchTurn()
	{
		var dispatcher = new QueuedDispatcher();
		var events = new List<string>();
		LauncherKitScopeReloadService.PostConfigurationReload(dispatcher, () =>
		{
			events.Add("replace");
			dispatcher.Post(() => events.Add("consumer"));
		}, () => events.Add("shell"));

		dispatcher.Drain();

		Assert.That(events, Is.EqualTo(new[] { "replace", "shell", "consumer" }));
	}

	private static LauncherKitDesktopOptions CreateOptions(Action refreshMainWindow, Action reloadViews) => new()
	{
		GetMainWindow = () => null,
		GetLifetime = () => throw new InvalidOperationException("No lifetime is needed for reload tests."),
		GetCurrentServices = () => throw new InvalidOperationException("No services are needed for reload tests."),
		GetCloseHandlers = () => [],
		RefreshMainWindow = refreshMainWindow,
		ReloadViews = reloadViews,
	};

	private sealed class SignalingDispatcher : IUiDispatcher
	{
		public TaskCompletionSource<Action> Posted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
		public void Post(Action action) => Posted.TrySetResult(action);
	}

	private sealed class QueuedDispatcher : IUiDispatcher
	{
		private readonly Queue<Action> _actions = new();
		public void Post(Action action) => _actions.Enqueue(action);
		public void Drain()
		{
			while (_actions.TryDequeue(out var action)) action();
		}
	}
}
