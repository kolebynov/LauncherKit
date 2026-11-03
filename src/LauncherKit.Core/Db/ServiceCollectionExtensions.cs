using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using LauncherKit.Core.Extensions;
using LauncherKit.Core.Services.Abstractions;
using LauncherKit.Infrastructure;

namespace LauncherKit.Core.Db;

internal static class ServiceCollectionExtensions
{
	public static void AddDbContexts(this IServiceCollection services)
	{
		AddGlobalDbContexts(services);
		AddSkyrimLocalDbContext(services);
	}

	private static void AddGlobalDbContexts(IServiceCollection services)
	{
		services.TryAddScoped(sp =>
		{
			var launchersRootDirectoryManager = sp.GetRequiredService<ILaunchersRootDirectoryManager>();
			return new GlobalDbContext(Path.Combine(launchersRootDirectoryManager.Directory,
				LauncherConstants.GlobalDatabaseFileName));
		});

		services.AddEarlyGlobalInitializer(sp =>
		{
			using var enteredScope = sp.GetRequiredService<IGlobalScope>().EnterScope();
			enteredScope.ServiceProvider.GetRequiredService<IDbMigrator<GlobalDbContext>>().Migrate();
		});

		services.TryAddScoped<IDbMigrator<GlobalDbContext>, DbMigrator<GlobalDbContext>>();

		services.TryAddSingleton<IMigrationProvider<GlobalDbContext>, GlobalMigrationProvider>();
	}

	private static void AddSkyrimLocalDbContext(IServiceCollection services)
	{
		services.TryAddScoped(sp =>
		{
			var product = sp.GetRequiredService<IOptions<LauncherProductOptions>>().Value;
			var appDataDirectory = sp.GetRequiredService<IOptionsSnapshot<AppSettings>>().Value.GetSkyrimAppDataDirectory(product);
			if (string.IsNullOrEmpty(appDataDirectory))
			{
				throw new InvalidOperationException("Skyrim directory is not set, cannot create database");
			}

			return new SkyrimLocalDbContext(Path.Combine(appDataDirectory, product.LocalDatabaseFileName));
		});

		services.AddEarlyConfiguredScopeInitializer(sp => sp.GetRequiredService<IDbMigrator<SkyrimLocalDbContext>>().Migrate());

		services.TryAddScoped<IDbMigrator<SkyrimLocalDbContext>, DbMigrator<SkyrimLocalDbContext>>();

		services.TryAddSingleton<IMigrationProvider<SkyrimLocalDbContext>, SkyrimLocalMigrationProvider>();
	}
}