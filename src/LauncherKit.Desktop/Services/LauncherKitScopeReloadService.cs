using Microsoft.Extensions.Options;
using LauncherKit.Desktop.Localization;
using LauncherKit.Core.Extensions;
using LauncherKit.Core.Initialization;
using LauncherKit.Infrastructure;

namespace LauncherKit.Desktop.Services;

/// <summary>
/// Owns the application scope, which belongs to the Skyrim directory: the scope is replaced only when
/// <see cref="AppSettings.SkyrimDirectory"/> changes. A language change only reloads the views.
/// </summary>
internal sealed class LauncherKitScopeReloadService : IGlobalInitializer, IDisposable
{
	private readonly IOptionsMonitor<AppSettings> _settings;
	private readonly IUiDispatcher _dispatcher;
	private readonly IAvaloniaApplicationLifetime _applicationLifetime;
	private readonly Action _requestConfigurationReload;
	private IDisposable? _subscription;
	private bool _disposed;

	public LauncherKitScopeReloadService(IOptionsMonitor<AppSettings> settings, IUiDispatcher dispatcher,
		IAvaloniaApplicationLifetime applicationLifetime, TimeProvider timeProvider)
	{
		_settings = settings;
		_dispatcher = dispatcher;
		_applicationLifetime = applicationLifetime;
		_requestConfigurationReload = DelegateExtensions.Debounce(
			PostConfigurationReload,
			TimeSpan.FromMilliseconds(200), timeProvider);
	}

	public void Initialize()
	{
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
					_applicationLifetime.ReloadViews();
				});
			}
			if (settings.SkyrimDirectory != oldSkyrimDirectory)
			{
				oldSkyrimDirectory = settings.SkyrimDirectory;
				_requestConfigurationReload();
			}
		});
	}

	private void PostConfigurationReload()
	{
		_dispatcher.Post(() =>
		{
			if (_disposed) return;
			_applicationLifetime.CreateNewScope();
		});
	}

	public void Dispose()
	{
		_disposed = true;
		_subscription?.Dispose();
	}
}
