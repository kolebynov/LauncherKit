using LauncherKit.FeedEditor.Storage;

namespace LauncherKit.FeedEditor.Extensions;

public static class ServiceCollectionExtensions
{
	public static IServiceCollection AddFeedEditor(this IServiceCollection services, IConfiguration configuration)
	{
		services.AddOptions<R2Settings>()
			.Bind(configuration.GetSection(R2Settings.SectionName))
			.ValidateDataAnnotations()
			.ValidateOnStart();
		services.AddSingleton<IR2ClientFactory, R2ClientFactory>();
		services.AddScoped<FeedSession>();
		return services;
	}
}
