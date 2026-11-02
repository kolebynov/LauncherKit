using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using R3;
using LauncherKit.Core.Extensions;
using LauncherKit.Core.Services.Abstractions;
using LauncherKit.Core.UserSettings;
using LauncherKit.Infrastructure;

namespace LauncherKit.Core.Services;

public sealed partial class SettingsService : ISettingsService, IDisposable
{
	private static readonly TimeSpan OptionsReloadTimeout = TimeSpan.FromSeconds(1);

	private readonly IOptionsMonitor<AppSettings> _appSettingsMonitor;
	private readonly ISettingsFileStore _settingsFileStore;
	private readonly TimeProvider _timeProvider;
	private readonly ILogger<SettingsService> _logger;
	private readonly SemaphoreSlim _lock = new(1, 1);
	private AllSettings _settings;

	public SettingsService(
		IOptionsMonitor<AppSettings> appSettingsMonitor,
		IOptionsMonitor<DownloadSettings> downloadSettingsMonitor,
		IOptionsMonitor<WindowSettings> windowSettingsMonitor,
		ISettingsFileStore settingsFileStore,
		TimeProvider timeProvider,
		ILogger<SettingsService> logger)
	{
		_appSettingsMonitor = appSettingsMonitor;
		_settingsFileStore = settingsFileStore;
		_timeProvider = timeProvider;
		_logger = logger;
		_settings = new AllSettings
		{
			App = appSettingsMonitor.CurrentValue,
			Download = downloadSettingsMonitor.CurrentValue,
			Window = windowSettingsMonitor.CurrentValue,
		};
	}

#pragma warning disable CA1024
	public AllSettings GetSettings() => _settings;
#pragma warning restore CA1024

	public async Task SaveSettings(AllSettings settings)
	{
		await _lock.WaitAsync();
		try
		{
			await SaveSettingsCore(settings);
		}
		finally
		{
			_lock.Release();
		}
	}

	public async Task ModifySettings(Func<AllSettings, ValueTask> modify)
	{
		await _lock.WaitAsync();
		try
		{
			var settings = _settings;
			await modify(settings);
			await SaveSettingsCore(settings);
		}
		finally
		{
			_lock.Release();
		}
	}

	public void Dispose() => _lock.Dispose();

	private async Task SaveSettingsCore(AllSettings settings)
	{
		var tcs = new TaskCompletionSource();
		using (_appSettingsMonitor.ToObservable()
			.Debounce(TimeSpan.FromMilliseconds(100))
			.Subscribe(_ => tcs.TrySetResult()))
		{
			var json = JsonSerializer.Serialize(settings, SettingsJsonContext.Default.AllSettings);
			await _settingsFileStore.Write(json, CancellationToken.None);
			try
			{
				await tcs.Task.WaitAsync(OptionsReloadTimeout, _timeProvider);
			}
			catch (TimeoutException)
			{
				// The file is already written; a missed reload must not hang callers such as app closing.
				_logger.LogWarning("Settings options were not reloaded within {Timeout}", OptionsReloadTimeout);
			}
		}

		_settings = settings;
	}

	[JsonSerializable(typeof(AllSettings))]
	[JsonSourceGenerationOptions(WriteIndented = true, UseStringEnumConverter = true)]
	private sealed partial class SettingsJsonContext : JsonSerializerContext
	{
	}
}
