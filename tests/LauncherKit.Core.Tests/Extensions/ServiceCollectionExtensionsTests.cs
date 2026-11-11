using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NuGet.Versioning;
using Polly.Timeout;
using LauncherKit.Core.Db;
using LauncherKit.Core.AppTasks;
using LauncherKit.Core.Extensions;
using LauncherKit.Core.Feed;
using LauncherKit.Core.FileStorage;
using LauncherKit.Core.Services.Abstractions;
using LauncherKit.Core.Settings;
using LauncherKit.Core.Update;
using LauncherKit.Core.UserSettings;
using LauncherKit.Infrastructure;
using LauncherKit.TestSupport;

namespace LauncherKit.Core.Tests.Extensions;

[TestFixture]
public sealed class ServiceCollectionExtensionsTests
{
	[Test]
	public async Task AddLauncherKitCore_MinimalConfiguration_ResolvesPublicServiceContracts()
	{
		using var temp = new TempDirectory();
		var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
		{
			["App:AppDataDirectory"] = temp.Path,
			["App:SkyrimDirectory"] = temp.Path,
			["Feed:FeedPath"] = "feed.json",
		}).Build();
		var mocker = new AutoMocker(MockBehavior.Strict);
		mocker.GetMock<ILaunchersRootDirectoryManager>().SetupGet(x => x.Directory).Returns(temp.Path);
		mocker.GetMock<IGlobalKeyValueStorage>().Setup(x => x.Get(It.IsAny<string>())).Returns((string?)null);
		mocker.GetMock<IAppVersionProvider>().SetupGet(x => x.Version).Returns(NuGetVersion.Parse("1.0.0"));
		var services = new ServiceCollection();
		services.AddLogging();
		services.AddSingleton(mocker.GetMock<ILaunchersRootDirectoryManager>().Object);
		services.AddSingleton(mocker.GetMock<IGlobalKeyValueStorage>().Object);
		services.AddSingleton(mocker.GetMock<IAppVersionProvider>().Object);
		services.AddSingleton(mocker.GetMock<IApplicationLifetime>().Object);
		services.AddSingleton(mocker.GetMock<IExternalBrowser>().Object);
		services.AddLauncherKitCore(configuration, ProductTestSupport.CreateProduct());
		await using var provider = services.BuildServiceProvider();
		await using var scope = provider.CreateAsyncScope();
		var scoped = scope.ServiceProvider;

		var resolved = new object[]
		{
			scoped.GetRequiredService<IAppTaskQueue>(),
			scoped.GetRequiredService<IFeedProvider>(),
			scoped.GetRequiredService<IModOrganizerService>(),
			scoped.GetRequiredService<IFileStorageService>(),
			scoped.GetRequiredService<ISettingsService>(),
			scoped.GetRequiredService<IUpdateProvider>(),
		};

		Assert.That(resolved, Has.All.Not.Null);
	}

	[Test]
	public async Task AddLauncherKitCore_FullConfiguration_BindsEveryOptionsClass()
	{
		// Arrange
		using var temp = new TempDirectory();
		var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
		{
			["App:AppDataDirectory"] = temp.Path,
			["App:SkyrimDirectory"] = temp.Path,
			["Feed:FeedPath"] = "feedV12.json",
			["Urls:Feed:MainUrl"] = "https://main.example",
			["Urls:Feed:ProxyUrl"] = "https://proxy.example",
			["Urls:Analytics:MainUrl"] = "https://analytics.example",
			["Urls:Analytics:ProxyUrl"] = "https://proxy.analytics.example",
			["Download:FileStoragePeriod"] = "Month",
			["Download:MaxDownloadSpeedBytesPerSecond"] = "1048576",
			["Window:Width"] = "1280.5",
			["Window:IsMaximized"] = "true",
			["Window:PositionX"] = "-8",
			["Discord:ClientId"] = "1",
			["Discord:GuildId"] = "2",
			["Discord:RedirectPort"] = "53134",
		}).Build();
		var services = new ServiceCollection();
		services.AddLogging();
		services.AddSingleton<IConfiguration>(configuration);
		services.AddLauncherKitCore(configuration, ProductTestSupport.CreateProduct());
		await using var provider = services.BuildServiceProvider();

		// Act
		var app = provider.GetRequiredService<IOptions<AppSettings>>().Value;
		var feed = provider.GetRequiredService<IOptions<FeedSettings>>().Value;
		var urls = provider.GetRequiredService<IOptions<UrlsSettings>>().Value;
		var download = provider.GetRequiredService<IOptions<DownloadSettings>>().Value;
		var window = provider.GetRequiredService<IOptions<WindowSettings>>().Value;
		var discord = provider.GetRequiredService<IOptions<DiscordSettings>>().Value;

		// Assert
		Assert.Multiple(() =>
		{
			Assert.That(app.AppDataDirectory, Is.EqualTo(temp.Path));
			Assert.That(feed.FeedPath, Is.EqualTo("feedV12.json"));
			Assert.That(urls.Urls.Keys, Is.EquivalentTo(new[] { "Feed", "Analytics" }));
			Assert.That(urls.Urls["Feed"].MainUrl, Is.EqualTo(new Uri("https://main.example")));
			Assert.That(urls.Urls["Analytics"].ProxyUrl, Is.EqualTo(new Uri("https://proxy.analytics.example")));
			Assert.That(download.FileStoragePeriod, Is.EqualTo(FileStoragePeriod.Month));
			Assert.That(download.PartialFileStoragePeriod, Is.EqualTo(FileStoragePeriod.Day));
			Assert.That(download.MaxDownloadSpeedBytesPerSecond, Is.EqualTo(1048576));
			Assert.That(window.Width, Is.EqualTo(1280.5));
			Assert.That(window.Height, Is.EqualTo(700));
			Assert.That(window.IsMaximized, Is.True);
			Assert.That(window.PositionX, Is.EqualTo(-8));
			Assert.That(window.PositionY, Is.Null);
			Assert.That(discord.ClientId, Is.EqualTo("1"));
			Assert.That(discord.GuildId, Is.EqualTo("2"));
			Assert.That(discord.RedirectPort, Is.EqualTo(53134));
		});
	}
	[Test]
	public void AddLauncherKitCore_InvalidIdentity_ThrowsBeforeRegisteringServices()
	{
		var services = new ServiceCollection();
		var configuration = new ConfigurationBuilder().Build();
		Assert.That(() => services.AddLauncherKitCore(configuration, ProductTestSupport.CreateProduct(skyrimDataDirectoryName: "")), Throws.ArgumentException);
		Assert.That(services, Is.Empty);
	}

	[Test]
	public async Task AddLauncherKitCore_IndependentProducts_IsolatesOptionsAndLocalDatabasePaths()
	{
		using var temp = new TempDirectory();
		var first = ProductTestSupport.CreateProduct();
		var second = ProductTestSupport.CreateProduct(
			skyrimDataDirectoryName: ".OtherLauncher",
			localDatabaseFileName: "other-local.db");
		var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
		{
			["App:SkyrimDirectory"] = temp.Path,
			["Product:SkyrimDataDirectoryName"] = "MustNotOverrideConsumer",
		}).Build();
		foreach (var product in new[] { first, second })
		{
			var root = ProductTestSupport.CreateMocker().GetMock<ILaunchersRootDirectoryManager>();
			root.SetupGet(x => x.Directory).Returns(temp.Path);
			var services = new ServiceCollection();
			services.AddLogging();
			services.AddSingleton(root.Object);
			services.AddLauncherKitCore(configuration, product);
			await using var provider = services.BuildServiceProvider();
			await using var scope = provider.CreateAsyncScope();
			var scoped = scope.ServiceProvider;
			Assert.That(scoped.GetRequiredService<IOptions<LauncherProductOptions>>().Value, Is.SameAs(product));
			using (var command = scoped.GetRequiredService<GlobalDbContext>().CreateCommand("CREATE TABLE IF NOT EXISTS IdentityMarker (Id INTEGER)"))
				command.ExecuteNonQuery();
			using (var command = scoped.GetRequiredService<SkyrimLocalDbContext>().CreateCommand("CREATE TABLE IdentityMarker (Id INTEGER)"))
				command.ExecuteNonQuery();
			Assert.Multiple(() =>
			{
				Assert.That(File.Exists(Path.Combine(temp.Path, LauncherConstants.GlobalDatabaseFileName)), Is.True);
				Assert.That(File.Exists(Path.Combine(temp.Path, product.SkyrimDataDirectoryName, product.LocalDatabaseFileName)), Is.True);
			});
		}
		Assert.That(File.Exists(Path.Combine(temp.Path, ".ExampleLauncher", "other-local.db")), Is.False);
	}

	[Test]
	public async Task ReplaceService_ScopedRegistration_ResolvesReplacementPerScopeInSamePosition()
	{
		var services = new ServiceCollection();
		services.AddSingleton<OtherService>();
		services.AddScoped<BaseService>();
		services.AddSingleton<OtherService>();

		services.ReplaceService<BaseService, DerivedService>();

		Assert.Multiple(() =>
		{
			Assert.That(services[1].ServiceType, Is.EqualTo(typeof(BaseService)));
			Assert.That(services[1].Lifetime, Is.EqualTo(ServiceLifetime.Scoped));
		});
		await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
		await using var first = provider.CreateAsyncScope();
		await using var second = provider.CreateAsyncScope();
		var resolved = first.ServiceProvider.GetRequiredService<BaseService>();
		Assert.Multiple(() =>
		{
			Assert.That(resolved, Is.TypeOf<DerivedService>());
			Assert.That(first.ServiceProvider.GetRequiredService<BaseService>(), Is.SameAs(resolved));
			Assert.That(second.ServiceProvider.GetRequiredService<BaseService>(), Is.Not.SameAs(resolved));
		});
	}

	[Test]
	public async Task ReplaceService_FactoryForTransientRegistration_CreatesNewReplacementEachTime()
	{
		var services = new ServiceCollection();
		services.AddTransient<BaseService>();

		services.ReplaceService<BaseService>(_ => new DerivedService());

		await using var provider = services.BuildServiceProvider();
		var resolved = provider.GetRequiredService<BaseService>();
		Assert.Multiple(() =>
		{
			Assert.That(resolved, Is.TypeOf<DerivedService>());
			Assert.That(provider.GetRequiredService<BaseService>(), Is.Not.SameAs(resolved));
		});
	}

	[Test]
	public void ReplaceService_NotRegistered_Throws()
	{
		var services = new ServiceCollection();
		services.AddKeyedScoped<BaseService>("key");

		Assert.That(() => services.ReplaceService<BaseService, DerivedService>(), Throws.InvalidOperationException);
	}

	[Test]
	public void ReplaceService_SeveralRegistrations_ThrowsWithoutChangingThem()
	{
		var services = new ServiceCollection();
		services.AddScoped<BaseService>();
		services.AddScoped<BaseService>();

		Assert.Multiple(() =>
		{
			Assert.That(() => services.ReplaceService<BaseService>(_ => new DerivedService()), Throws.InvalidOperationException);
			Assert.That(services.Select(x => x.ImplementationType), Has.All.EqualTo(typeof(BaseService)));
		});
	}

	[Test]
	public async Task S3HttpClient_BodyStallsAfterHeaders_TimesOut()
	{
		// Arrange
		var timeProvider = new FakeTimeProvider();
		var bodyReadStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		await using var provider = BuildProviderWithStallingServer(AppConstants.S3HttpClient, timeProvider, bodyReadStarted);
		using var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient(AppConstants.S3HttpClient);

		// Act
		var request = client.GetStringAsync(new Uri("https://main.example/feed.json"));
		// The resilience timeout is started before the body read, so the clock can move past it now.
		await bodyReadStarted.Task;
		timeProvider.Advance(TimeSpan.FromSeconds(15));

		// Assert
		// The real-time limit only keeps a broken timeout from hanging the run.
		Assert.That(async () => await request.WaitAsync(TimeSpan.FromSeconds(30)), Throws.InstanceOf<TimeoutRejectedException>());
	}

	[Test]
	public async Task S3DownloadHttpClient_BodyStallsAfterHeaders_ReturnsResponseWithoutReadingBody()
	{
		// Arrange
		await using var provider = BuildProviderWithStallingServer(
			AppConstants.S3DownloadHttpClient, new FakeTimeProvider(), new TaskCompletionSource());
		using var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient(AppConstants.S3DownloadHttpClient);
		using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("https://main.example/file.7z"));

		// Act
		using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead)
			.WaitAsync(TimeSpan.FromSeconds(10));

		// Assert
		Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
	}

	/// <summary>Builds the core services whose <paramref name="clientName"/> client talks to a server that sends the headers and then stalls the body.</summary>
	private static ServiceProvider BuildProviderWithStallingServer(
		string clientName, TimeProvider timeProvider, TaskCompletionSource bodyReadStarted)
	{
		var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
		{
			["Urls:Feed:MainUrl"] = "https://main.example",
		}).Build();
		var mocker = new AutoMocker(MockBehavior.Strict);
		mocker.GetMock<IAppVersionProvider>().SetupGet(x => x.Version).Returns(NuGetVersion.Parse("1.0.0"));
		var services = new ServiceCollection();
		services.AddLogging();
		services.AddSingleton(timeProvider);
		services.AddSingleton(mocker.GetMock<IAppVersionProvider>().Object);
		services.AddLauncherKitCore(configuration, ProductTestSupport.CreateProduct());
		services.AddHttpClient(clientName).ConfigurePrimaryHttpMessageHandler(() => new StallingBodyHandler(bodyReadStarted));
		return services.BuildServiceProvider();
	}

	private sealed class StallingBodyHandler(TaskCompletionSource bodyReadStarted) : HttpMessageHandler
	{
		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			var content = new StreamContent(new StallingStream(bodyReadStarted));
			content.Headers.ContentLength = 1000;
			return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content, RequestMessage = request });
		}
	}

	/// <summary>A body whose reads never complete until cancelled; the first read completes <c>readStarted</c>.</summary>
	private sealed class StallingStream(TaskCompletionSource readStarted) : Stream
	{
		public override bool CanRead => true;
		public override bool CanSeek => false;
		public override bool CanWrite => false;
		public override long Length => throw new NotSupportedException();
		public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

		public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
		{
			readStarted.TrySetResult();
			await Task.Delay(Timeout.Infinite, cancellationToken);
			return 0;
		}

		public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
			ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

		public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
		public override void Flush() => throw new NotSupportedException();
		public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
		public override void SetLength(long value) => throw new NotSupportedException();
		public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
	}

	private sealed class OtherService;

	private class BaseService;

	private sealed class DerivedService : BaseService;
}
