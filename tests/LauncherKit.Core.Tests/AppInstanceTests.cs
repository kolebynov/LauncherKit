using LauncherKit.Core.Extensions;
using LauncherKit.Core.Initialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using LauncherKit.Infrastructure;

namespace LauncherKit.Core.Tests;

[TestFixture]
public sealed class AppInstanceTests
{
	[Test]
	public void CreateNewScope_DisposesPreviousScopeSynchronously()
	{
		var services = new ServiceCollection().AddLogging();
		services.AddOptions<AppSettings>();
		services.AddScoped<DisposalProbe>();
		using var sut = new AppInstance(services.BuildServiceProvider(), new ConfigurationManager());
		sut.Initialize();
		var previousScopeProbe = sut.ServiceProvider.GetRequiredService<DisposalProbe>();

		sut.CreateNewScope();

		Assert.Multiple(() =>
		{
			Assert.That(previousScopeProbe.IsDisposed, Is.True);
			Assert.That(sut.ServiceProvider.GetRequiredService<DisposalProbe>(), Is.Not.SameAs(previousScopeProbe));
		});
	}

	[Test]
	public void CreateNewScope_WithoutSkyrimDirectory_RunsOnlyScopeInitializers()
	{
		var services = new ServiceCollection().AddLogging();
		services.AddOptions<AppSettings>();
		services.AddSingleton<InitializerLog>();
		services.AddScopeInitializer<ScopeProbe>();
		services.AddConfiguredScopeInitializer<ConfiguredScopeProbe>();
		using var sut = new AppInstance(services.BuildServiceProvider(), new ConfigurationManager());
		sut.Initialize();

		sut.CreateNewScope();

		Assert.That(sut.ServiceProvider.GetRequiredService<InitializerLog>().Entries, Is.EqualTo(new[] { "scope", "scope" }));
	}

	private sealed class InitializerLog
	{
		public List<string> Entries { get; } = [];
	}

	private sealed class ScopeProbe(InitializerLog log) : IScopeInitializer
	{
		public void Initialize() => log.Entries.Add("scope");
	}

	private sealed class ConfiguredScopeProbe(InitializerLog log) : IConfiguredScopeInitializer
	{
		public void Initialize() => log.Entries.Add("configured");
	}

	private sealed class DisposalProbe : IDisposable
	{
		public bool IsDisposed { get; private set; }

		public void Dispose() => IsDisposed = true;
	}
}
