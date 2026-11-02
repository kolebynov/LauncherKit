namespace LauncherKit.Infrastructure;

/// <summary>Required consumer-supplied local product identity. No product names are inferred.</summary>
public sealed class LauncherProductOptions
{
	public string RootDirectoryName { get; set; } = string.Empty;
	public string SkyrimDataDirectoryName { get; set; } = string.Empty;
	public string GlobalDatabaseFileName { get; set; } = string.Empty;
	public string LocalDatabaseFileName { get; set; } = string.Empty;
	public string OAuthTokenEntropy { get; set; } = string.Empty;
	public string EnvironmentVariablePrefix { get; set; } = string.Empty;
	public string AppExecutableName { get; set; } = string.Empty;
	public string DisplayName { get; set; } = string.Empty;
	public string UserAgentProduct { get; set; } = string.Empty;
	public string ModOrganizerDirectoryName { get; set; } = string.Empty;
	public string ModpackProfileName { get; set; } = string.Empty;
	public string ModpackVersionFile { get; set; } = string.Empty;
	public string UserModsSeparator { get; set; } = string.Empty;
	public string RequiredSkyrimVersion { get; set; } = string.Empty;
	/// <summary>Registry key under which the consumer's installer reads the launcher's data directories, e.g. <c>HKEY_CURRENT_USER\Software\Company\Product</c>.</summary>
	public string RegistryKey { get; set; } = string.Empty;
	public Uri? FirstSetupHelpUri { get; set; }
	public IReadOnlyList<GameLaunchVariant> GameLaunchVariants { get; set; } = [];

	/// <summary>Rejects an incomplete identity before configuration or installation state is accessed.</summary>
	public void Validate()
	{
		Require(RootDirectoryName, nameof(RootDirectoryName));
		Require(SkyrimDataDirectoryName, nameof(SkyrimDataDirectoryName));
		Require(GlobalDatabaseFileName, nameof(GlobalDatabaseFileName));
		Require(LocalDatabaseFileName, nameof(LocalDatabaseFileName));
		Require(OAuthTokenEntropy, nameof(OAuthTokenEntropy));
		Require(EnvironmentVariablePrefix, nameof(EnvironmentVariablePrefix));
		Require(AppExecutableName, nameof(AppExecutableName));
		Require(DisplayName, nameof(DisplayName));
		Require(UserAgentProduct, nameof(UserAgentProduct));
		Require(ModOrganizerDirectoryName, nameof(ModOrganizerDirectoryName));
		Require(ModpackProfileName, nameof(ModpackProfileName));
		Require(ModpackVersionFile, nameof(ModpackVersionFile));
		Require(UserModsSeparator, nameof(UserModsSeparator));
		Require(RequiredSkyrimVersion, nameof(RequiredSkyrimVersion));
		Require(RegistryKey, nameof(RegistryKey));
		if (GameLaunchVariants is not { Count: > 0 })
		{
			throw new ArgumentException("Consumer product identity requires game launch variants.", nameof(GameLaunchVariants));
		}

		foreach (var variant in GameLaunchVariants)
		{
			if (variant is null)
			{
				throw new ArgumentException("Consumer product identity contains a null game launch variant.", nameof(GameLaunchVariants));
			}
			Require(variant.Id, nameof(GameLaunchVariants));
		}

		static void Require(string value, string name)
		{
			if (string.IsNullOrWhiteSpace(value))
			{
				throw new ArgumentException($"Consumer product identity requires {name}.", name);
			}
		}
	}
}

public sealed class GameLaunchVariant
{
	public string Id { get; set; } = string.Empty;
	public string? ExecutablePath { get; set; }
}
