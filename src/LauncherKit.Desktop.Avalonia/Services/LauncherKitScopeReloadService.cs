using Microsoft.Extensions.Options;
using LauncherKit.Desktop.Localization;
using LauncherKit.Desktop.Services;
using LauncherKit.Core.Extensions;
using LauncherKit.Infrastructure;

namespace LauncherKit.Desktop.Avalonia.Services;

/// <summary>Owns settings-driven scope replacement, preserving execution ownership for language-only reloads.</summary>
public sealed class LauncherKitScopeReloadService : IDisposable
{
	private readonly IOptionsMonitor<AppSettings> _settings;
	private readonly IUiDispatcher _dispatcher;
	private readonly LauncherKitDesktopOptions _desktop;
	private readonly Action _requestConfigurationReload;
	private IDisposable? _subscription;
	private Action? _replaceScope;
	private bool _disposed;

	public LauncherKitScopeReloadService(IOptionsMonitor<AppSettings> settings, IUiDispatcher dispatcher,
		LauncherKitDesktopOptions desktop, TimeProvider timeProvider)
	{
		_settings = settings;
		_dispatcher = dispatcher;
		_desktop = desktop;
		_requestConfigurationReload = DelegateExtensions.Debounce(() =>
			PostConfigurationReload(dispatcher, () =>
			{
				if (!_disposed) _replaceScope!();
			}, () =>
			{
				if (!_disposed) _desktop.RefreshMainWindow();
			}), TimeSpan.FromMilliseconds(200), timeProvider);
	}

	/// <summary>Attaches after the initial application scope has been published.</summary>
	public void Start(Action replaceScope)
	{
		if (_replaceScope is not null) throw new InvalidOperationException("Scope reload is already attached.");
		_replaceScope = replaceScope;
		var oldSkyrimDirectory = _settings.CurrentValue.SkyrimDirectory;
		var oldLanguage = _settings.CurrentValue.Language;
		_subscription = _settings.OnChange(settings =>
		{
			if (settings.Language != oldLanguage)
			{
				oldLanguage = settings.Language;
				LanguageCulture.Apply(oldLanguage);
				_dispatcher.Post(() =>
				{
					if (_disposed) return;
					LanguageCulture.ApplyToCurrentThread();
					_desktop.ReloadViews();
				});
			}
			if (settings.SkyrimDirectory != oldSkyrimDirectory)
			{
				oldSkyrimDirectory = settings.SkyrimDirectory;
				RequestConfigurationReload();
			}
		});
	}

	public void RequestConfigurationReload() => _requestConfigurationReload();

	internal static void PostConfigurationReload(IUiDispatcher dispatcher, Action replaceScope, Action refreshMainWindow)
	{
		dispatcher.Post(() =>
		{
			// Disposal, scope publication and main window view model replacement share one UI turn.
			replaceScope();
			refreshMainWindow();
		});
	}

	public void Dispose()
	{
		_disposed = true;
		_subscription?.Dispose();
	}
}
