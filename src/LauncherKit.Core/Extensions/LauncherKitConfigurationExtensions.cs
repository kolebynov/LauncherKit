using Microsoft.Extensions.Configuration;
using LauncherKit.Infrastructure;

namespace LauncherKit.Core.Extensions;

public static class LauncherKitConfigurationExtensions
{
	/// <summary>Adds launcher settings in JSON, local override, then product-prefixed environment order.</summary>
	public static ConfigurationManager AddLauncherKitConfiguration(this ConfigurationManager configuration, string basePath,
		LauncherProductOptions product)
	{
		ArgumentNullException.ThrowIfNull(product);
		product.Validate();
		configuration.SetBasePath(basePath);
		configuration.AddJsonFile("appsettings.json", false, true);
		configuration.AddJsonFile(LauncherConstants.SettingsOverrideFileName, true, true);
		// Identity is supplied before loading configuration, so the prefix cannot change mid-composition.
		configuration.AddEnvironmentVariables(product.EnvironmentVariablePrefix);
		return configuration;
	}
}
