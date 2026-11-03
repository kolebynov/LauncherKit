using System.Reflection;
using System.Resources;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;

namespace LauncherKit.Desktop.Localization;

public static class LocalizationServiceCollectionExtensions
{
	/// <summary>
	/// Adds desktop strings with optional consumer overrides named
	/// &lt;consumerAssembly&gt;.Localization.LauncherKit.DesktopStrings, and consumer view strings.
	/// Each culture resolves consumer, then kit, before moving to its parent and finally neutral resources.
	/// </summary>
	public static IServiceCollection AddLauncherKitLocalization(this IServiceCollection services, Assembly? overridesAssembly = null)
	{
		ArgumentNullException.ThrowIfNull(services);
		services.AddLogging();
		services.AddSingleton<IStringLocalizer<DesktopStrings>>(provider =>
			new ExactResourceStringLocalizer<DesktopStrings>(
				new ResourceManager("LauncherKit.Desktop.Localization.DesktopStrings", typeof(DesktopStrings).Assembly),
				overridesAssembly is null ? null : new ResourceManager(
					$"{overridesAssembly.GetName().Name}.Localization.LauncherKit.DesktopStrings", overridesAssembly),
				provider.GetRequiredService<ILogger<ExactResourceStringLocalizer<DesktopStrings>>>()));
		services.AddSingleton<TaskTexts>();
		services.AddSingleton<ChoiceTexts>();
		services.AddSingleton<ExceptionTexts>();
		services.AddSingleton<EnumTexts>();
		services.AddSingleton<HomeCardFactTexts>();
		services.AddSingleton<FileDeleteResultTexts>();
		services.AddSingleton<SetupAccountFactTexts>();
		return services;
	}
}
