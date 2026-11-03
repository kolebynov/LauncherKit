using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using Duende.IdentityModel.OidcClient.Browser;
using IniParser;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using Polly;
using LauncherKit.Core.Analytics;
using LauncherKit.Core.AppTasks;
using LauncherKit.Core.AppTasks.InstallSteps;
using LauncherKit.Core.AppTasks.MicroTasks;
using LauncherKit.Core.Backups;
using LauncherKit.Core.Db;
using LauncherKit.Core.Discord;
using LauncherKit.Core.Feed;
using LauncherKit.Core.FileSources;
using LauncherKit.Core.FileStorage;
using LauncherKit.Core.Fomod;
using LauncherKit.Core.Initialization;
using LauncherKit.Core.OAuth;
using LauncherKit.Core.Services;
using LauncherKit.Core.Services.Abstractions;
using LauncherKit.Core.Settings;
using LauncherKit.Core.Update;
using LauncherKit.Core.UserSettings;
using LauncherKit.Core.Utils;
using LauncherKit.Infrastructure;
using FileSystemUtils = LauncherKit.Infrastructure.FileSystemUtils;

namespace LauncherKit.Core.Extensions;

public static class ServiceCollectionExtensions
{
	public static IServiceCollection AddLauncherKitCore(this IServiceCollection services, IConfiguration configuration,
		LauncherProductOptions product)
	{
		ArgumentNullException.ThrowIfNull(product);
		product.Validate();
		services.AddSingleton<IOptions<LauncherProductOptions>>(Options.Create(product));
		services.TryAddSingleton(configuration);
		services.AddDbContexts();

		services.AddHttpClient(AppConstants.AnalyticsHttpClient)
			.ConfigureHttpClient((sp, client) =>
			{
				var versionProvider = sp.GetRequiredService<IAppVersionProvider>();
				ConfigureCloudflareClient(client, versionProvider, sp, "Analytics");
			});

		services.AddHttpClient(AppConstants.DiscordHttpClient)
			.ConfigureHttpClient((sp, client) =>
			{
				var versionProvider = sp.GetRequiredService<IAppVersionProvider>();
				client.BaseAddress = DiscordApiClient.BaseUri;
				client.DefaultRequestHeaders.Add("User-Agent", $"{sp.GetRequiredService<IOptions<LauncherProductOptions>>().Value.UserAgentProduct} {versionProvider.Version}");
				client.Timeout = TimeSpan.FromSeconds(30);
			});

		services.AddHttpClient<IHttpFileSourceFactory, HttpFileSourceFactory>(AppConstants.HttpFileSourceHttpClient)
			.ConfigureHttpClient((sp, client) =>
			{
				var versionProvider = sp.GetRequiredService<IAppVersionProvider>();
				client.DefaultRequestHeaders.Add("User-Agent", $"{sp.GetRequiredService<IOptions<LauncherProductOptions>>().Value.UserAgentProduct} {versionProvider.Version}");
				client.Timeout = Timeout.InfiniteTimeSpan;
			})
			.UseSocketsHttpHandler()
			.AddResilienceHandler("Resilience", (builder, _) => builder
				.AddTimeout(GetDefaultTotalTimeoutOptions())
				.AddRetry(GetDefaultRetryOptions())
				.AddTimeout(GetDefaultAttemptTimeoutOptions()))
			.SelectPipelineByAuthority();

		services.AddHttpClient(AppConstants.S3HttpClient)
			.ConfigureHttpClient((sp, client) =>
			{
				var versionProvider = sp.GetRequiredService<IAppVersionProvider>();
				ConfigureCloudflareClient(client, versionProvider, sp, "Feed");
			})
			.UseSocketsHttpHandler((h, _) =>
			{
				h.AutomaticDecompression = DecompressionMethods.None;
				h.AllowAutoRedirect = true;
				h.EnableMultipleHttp2Connections = true;
				h.MaxConnectionsPerServer = int.MaxValue;
			})
			.AddResilienceHandler("Resilience", (builder, _) =>
			{
				var retryOptions = GetDefaultRetryOptions();
				var shouldHandle = retryOptions.ShouldHandle;
				retryOptions.ShouldHandle = arguments =>
				{
					var httpRequestKey = new ResiliencePropertyKey<HttpRequestMessage>("Resilience.Http.RequestMessage");
					if (arguments.Outcome.Result is not { } response ||
					    !arguments.Context.Properties.TryGetValue(httpRequestKey, out var request))
					{
						return shouldHandle(arguments);
					}

					return request.Headers.Range != null && response is { IsSuccessStatusCode: true, StatusCode: not HttpStatusCode.PartialContent }
						? ValueTask.FromResult(true)
						: shouldHandle(arguments);
				};

				builder
					.AddTimeout(GetDefaultTotalTimeoutOptions())
					.AddRetry(retryOptions)
					.AddTimeout(GetDefaultAttemptTimeoutOptions());
			});

		services.AddConfiguredScopeInitializer<LegacyPartialDownloadsInitializer>();
		services.AddConfiguredScopeInitializer<CleanupOldDataInitializer>();
		services.AddGlobalInitializer<DataDirectoriesRegistrationInitializer>();

		services.Configure<DownloadSettings>(configuration.GetSection("Download"));
		services.Configure<FeedSettings>(configuration.GetSection("Feed"));
		services.Configure<UrlsSettings>(configuration);
		services.Configure<AppSettings>(configuration.GetSection("App"));
		services.Configure<WindowSettings>(configuration.GetSection("Window"));
		services.AddOptions<DiscordSettings>()
			.Bind(configuration.GetSection("Discord"))
			.Validate(s => ulong.TryParse(s.ClientId, CultureInfo.InvariantCulture, out _),
				"Discord:ClientId must be the ID of the Discord application")
			.Validate(s => ulong.TryParse(s.GuildId, CultureInfo.InvariantCulture, out _),
				"Discord:GuildId must be the ID of the Discord server")
			.Validate(s => s.RedirectPort is > IPEndPoint.MinPort and <= IPEndPoint.MaxPort,
				"Discord:RedirectPort must be a TCP port");
		services.AddOptions<AppSettings>()
			.PostConfigure<IConfiguration, IFileSystemUtils, IOptions<LauncherProductOptions>>((opt, c, fileSystemUtils, product) =>
			{
				if (!string.IsNullOrEmpty(opt.SkyrimDirectory))
				{
					opt.SkyrimDirectory = fileSystemUtils.GetFullPath(opt.SkyrimDirectory, Environment.CurrentDirectory);
					fileSystemUtils.EnsureDirectoryExists(opt.SkyrimDirectory);
				}

				var skyrimAppDataDirectory = opt.GetSkyrimAppDataDirectory(product.Value);
				if (!string.IsNullOrEmpty(skyrimAppDataDirectory))
				{
					fileSystemUtils.EnsureDirectoryExists(skyrimAppDataDirectory);
				}
			});

		if (OperatingSystem.IsWindows())
		{
			services.TryAddSingleton<ISkyrimDirectoryFinder, WindowsSkyrimDirectoryFinder>();
		}
		else
		{
			services.TryAddSingleton<ISkyrimDirectoryFinder, NoopSkyrimDirectoryFinder>();
		}

		services.TryAddScoped<IAppTaskProvider, AppTaskProvider>();
		services.TryAddScoped<IAppTaskQueue, AppTaskQueue>();
		services.TryAddScoped<IFeedAvailabilityChecker, FeedAvailabilityChecker>();
		services.TryAddScoped<IFeedFileSourceFactory, FeedFileSourceFactory>();
		services.TryAddScoped<IFileStorageService, FileStorageService>();
		services.TryAddScoped<IFomodInstallService, FomodInstallService>();
		services.TryAddScoped<ISkyrimLocalKeyValueStorage, SkyrimLocalKeyValueStorage>();
		services.TryAddScoped<IMicroTasksFactory, MicroTasksFactory>();
		services.TryAddScoped<IInstallStepsMicroTasksFactory, InstallStepsMicroTasksFactory>();
		services.TryAddScoped<IInstallBackupService, InstallBackupService>();
		services.TryAddScoped<IInstallAppTaskProvider, InstallAppTaskProvider>();
		services.TryAddScoped<IPrerequisitesAppTaskProvider, PrerequisitesAppTaskProvider>();
		services.TryAddScoped<IModOrganizerService, ModOrganizerService>();
		services.TryAddScoped<ISettingsService, SettingsService>();
		services.TryAddScoped<IUpdateProvider, UpdateProvider>();

		services.TryAddSingleton(TimeProvider.System);
		services.TryAddSingleton<IComparer<string>>(NaturalStringComparer.Instance);
		services.TryAddSingleton<IAppLogsCollector, AppLogsCollector>();
		services.TryAddSingleton<IAppVersionProvider, AppVersionProvider>();
		services.TryAddSingleton<IDiskSpaceChecker, DiskSpaceChecker>();
		services.TryAddSingleton<IDownloadServerFallback, DownloadServerFallback>();
		services.TryAddSingleton<IDownloadRateLimiter, DownloadRateLimiter>();
		services.TryAddSingleton<IFeedProvider, FeedProvider>();
		services.TryAddSingleton<IBrowser, LoopbackBrowser>();
		services.TryAddSingleton<IDiscordAccountService, DiscordAccountService>();
		services.TryAddSingleton<IDiscordApiClient, DiscordApiClient>();
		services.TryAddSingleton<IDiscordOAuthClient, DiscordOAuthClient>();
		services.TryAddSingleton<IFeedUtils, FeedUtils>();
		services.TryAddSingleton<FileIniDataParser>();
		services.TryAddSingleton<IFileSourceDownloader, FileSourceDownloader>();
		services.TryAddSingleton<IFileSystemUtils, FileSystemUtils>();
		services.TryAddSingleton<IGlobalKeyValueStorage, GlobalKeyValueStorage>();
		services.TryAddSingleton<IGlobalScope, GlobalScope>();
		services.TryAddSingleton<IHashCalculator, HashCalculator>();
		services.TryAddSingleton<IInstallDetector, InstallDetector>();
		services.TryAddSingleton<LogsCollector.Core.ILogsCollector, LogsCollector.Core.LogsCollector>();
		services.TryAddEnumerable(ServiceDescriptor.Singleton<LogsCollector.Core.ILogsSource, LogsCollector.Core.LauncherLogsSource>());
		services.TryAddEnumerable(ServiceDescriptor.Singleton<LogsCollector.Core.ILogsSource, LogsCollector.Core.SkseLogsSource>());
		services.TryAddEnumerable(ServiceDescriptor.Singleton<LogsCollector.Core.ILogsSource, LogsCollector.Core.ModOrganizerLogsSource>());
		services.TryAddSingleton<IProcessRunner, ProcessRunner>();
		services.TryAddSingleton<IRegistryManager, RegistryManager>();
		services.TryAddSingleton<ISettingsFileStore, AppSettingsOverrideFileStore>();
		services.TryAddSingleton<IOAuthTokenStore, ProtectedOAuthTokenStore>();
		services.TryAddSingleton<ISevenZipService, SevenZipService>();
		services.TryAddSingleton<ISkyrimInstallationChecker, SkyrimInstallationChecker>();
		services.TryAddSingleton<IModpackVersionProvider, ModpackVersionProvider>();
		services.TryAddSingleton<ITempStorageService, TempStorageService>();
		services.TryAddSingleton<IInstallationIdProvider, InstallationIdProvider>();
		services.TryAddSingleton<IInstallationAnalyticsService, InstallationAnalyticsService>();
		services.TryAddSingleton<IUrlResolver, UrlResolver>();

		return services;
	}

	public static IServiceCollection AddInstallationAnalytics(this IServiceCollection services) =>
		services.AddGlobalInitializer<InstallationAnalyticsInitializer>();

	public static IServiceCollection AddConfiguredScopeInitializer(
		this IServiceCollection services, Action<IServiceProvider> initializer)
	{
		services.AddScoped<IConfiguredScopeInitializer>(sp => new DelegateInitializer(initializer, sp));
		return services;
	}

	public static IServiceCollection AddEarlyConfiguredScopeInitializer(
		this IServiceCollection services, Action<IServiceProvider> initializer)
	{
		services.AddScoped<IEarlyConfiguredScopeInitializer>(sp => new DelegateInitializer(initializer, sp));
		return services;
	}

	public static IServiceCollection AddConfiguredScopeInitializer<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>(
		this IServiceCollection services)
		where T : class, IConfiguredScopeInitializer
	{
		services.AddScoped<IConfiguredScopeInitializer, T>();
		return services;
	}

	public static IServiceCollection AddGlobalInitializer(
		this IServiceCollection services, Action<IServiceProvider> initializer)
	{
		services.AddSingleton<IGlobalInitializer>(sp => new DelegateInitializer(initializer, sp));
		return services;
	}

	public static IServiceCollection AddEarlyGlobalInitializer(
		this IServiceCollection services, Action<IServiceProvider> initializer)
	{
		services.AddSingleton<IEarlyGlobalInitializer>(sp => new DelegateInitializer(initializer, sp));
		return services;
	}

	public static IServiceCollection AddGlobalInitializer<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>(
		this IServiceCollection services)
		where T : class, IGlobalInitializer
	{
		services.AddSingleton<IGlobalInitializer, T>();
		return services;
	}

	private static void ConfigureCloudflareClient(HttpClient client, IAppVersionProvider versionProvider,
		IServiceProvider services, string urlName)
	{
		var urls = services.GetRequiredService<IOptions<UrlsSettings>>().Value;
		client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", urls.Urls[urlName].BearerToken);
		client.DefaultRequestHeaders.Add("User-Agent",
			$"{services.GetRequiredService<IOptions<LauncherProductOptions>>().Value.UserAgentProduct} {versionProvider.Version}");
		client.Timeout = Timeout.InfiniteTimeSpan;
	}

	private static HttpRetryStrategyOptions GetDefaultRetryOptions() => new()
	{
		Delay = TimeSpan.FromMilliseconds(500),
		MaxDelay = TimeSpan.FromSeconds(1),
		MaxRetryAttempts = 3,
		UseJitter = true,
	};

	private static HttpTimeoutStrategyOptions GetDefaultTotalTimeoutOptions() => new()
	{
		Timeout = TimeSpan.FromSeconds(30),
	};

	private static HttpTimeoutStrategyOptions GetDefaultAttemptTimeoutOptions() => new()
	{
		Timeout = TimeSpan.FromSeconds(10),
	};
}
